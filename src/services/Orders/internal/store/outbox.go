package store

import (
	"context"
	"database/sql"
	"fmt"
	"strings"
	"time"

	mssql "github.com/microsoft/go-mssqldb"

	"marketplace/orders/internal/outbox"
)

// Store também é o outbox.Store do relay.
var _ outbox.Store = (*Store)(nil)

// FetchPending devolve até max mensagens pendentes, na ordem de gravação.
func (s *Store) FetchPending(ctx context.Context, max int) ([]outbox.Message, error) {
	rows, err := s.db.QueryContext(ctx,
		`SELECT TOP (@p1) Sequence, EventId, Type, [Key], Payload, TraceParent, CreatedAt, Attempts
		 FROM dbo.OutboxMessages WHERE PublishedAt IS NULL ORDER BY Sequence`, max)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var messages []outbox.Message
	for rows.Next() {
		var m outbox.Message
		var payload string
		var traceParent sql.NullString
		if err := rows.Scan(&m.Sequence, &m.EventID, &m.Type, &m.Key, &payload, &traceParent, &m.CreatedAt, &m.Attempts); err != nil {
			return nil, err
		}
		m.Payload, m.TraceParent, m.CreatedAt = []byte(payload), traceParent.String, m.CreatedAt.UTC()
		messages = append(messages, m)
	}
	return messages, rows.Err()
}

// MarkPublished marca o lote num UPDATE só. Pela lista de sequências, nunca por faixa: uma transação mais lenta pode
// gravar uma sequência menor depois que o lote foi lido, e ela ainda não foi enviada.
func (s *Store) MarkPublished(ctx context.Context, sequences []int64, at time.Time) error {
	if len(sequences) == 0 {
		return nil
	}
	args := []any{mssql.DateTimeOffset(at.UTC())}
	placeholders := make([]string, len(sequences))
	for i, seq := range sequences {
		args = append(args, seq)
		placeholders[i] = fmt.Sprintf("@p%d", i+2)
	}
	_, err := s.db.ExecContext(ctx, `UPDATE dbo.OutboxMessages SET PublishedAt = @p1 WHERE Sequence IN (`+
		strings.Join(placeholders, ", ")+`)`, args...)
	return err
}

func (s *Store) MarkFailed(ctx context.Context, sequence int64, reason string) error {
	if len(reason) > 2000 {
		reason = reason[:2000]
	}
	_, err := s.db.ExecContext(ctx, `UPDATE dbo.OutboxMessages SET Attempts = Attempts + 1, LastError = @p2 WHERE Sequence = @p1`,
		sequence, reason)
	return err
}

func (s *Store) Stats(ctx context.Context) (outbox.Stats, error) {
	var stats outbox.Stats
	var oldest sql.NullTime
	err := s.db.QueryRowContext(ctx,
		`SELECT COUNT(*), MIN(CreatedAt) FROM dbo.OutboxMessages WHERE PublishedAt IS NULL`).Scan(&stats.Pending, &oldest)
	if oldest.Valid {
		stats.OldestPendingSince = oldest.Time.UTC()
	}
	return stats, err
}

func (s *Store) DeletePublishedBefore(ctx context.Context, before time.Time) (int64, error) {
	result, err := s.db.ExecContext(ctx, `DELETE FROM dbo.OutboxMessages WHERE PublishedAt IS NOT NULL AND PublishedAt < @p1`,
		mssql.DateTimeOffset(before.UTC()))
	if err != nil {
		return 0, err
	}
	return result.RowsAffected()
}

// lockResource é o nome do lock do relay no SQL Server (um por serviço/banco).
const lockResource = "outbox:orders"

// Lock é o lock do relay: sp_getapplock preso a uma sessão. Vale enquanto a conexão estiver aberta; se a instância
// cair, a sessão fecha, o lock é liberado e outra cópia do serviço assume.
func (s *Store) Lock() outbox.Lock { return sqlLock{db: s.lockDB} }

type sqlLock struct{ db *sql.DB }

func (l sqlLock) TryAcquire(ctx context.Context) (outbox.Lease, error) {
	conn, err := l.db.Conn(ctx)
	if err != nil {
		return nil, err
	}
	var result int
	err = conn.QueryRowContext(ctx,
		`DECLARE @result INT;
		 EXEC @result = sp_getapplock @Resource = @p1, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
		 SELECT @result`, lockResource).Scan(&result)
	if err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("sp_getapplock: %w", err)
	}
	// 0 = concedido, 1 = concedido depois de esperar; negativo = outra sessão tem o lock.
	if result < 0 {
		_ = conn.Close()
		return nil, nil
	}
	return &sqlLease{conn: conn}, nil
}

type sqlLease struct{ conn *sql.Conn }

func (l *sqlLease) Held(ctx context.Context) bool {
	var mode string
	err := l.conn.QueryRowContext(ctx, `SELECT APPLOCK_MODE('public', @p1, 'Session')`, lockResource).Scan(&mode)
	return err == nil && mode == "Exclusive"
}

// Release libera o lock e fecha a conexão (sem conexões ociosas no lockDB, a sessão termina de fato).
func (l *sqlLease) Release() error {
	_, _ = l.conn.ExecContext(context.Background(), `EXEC sp_releaseapplock @Resource = @p1, @LockOwner = 'Session'`, lockResource)
	return l.conn.Close()
}
