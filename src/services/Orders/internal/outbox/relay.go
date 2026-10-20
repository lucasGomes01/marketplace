package outbox

import (
	"context"
	"log/slog"
	"time"
)

// Options do relay; zero usa os padrões.
type Options struct {
	PollInterval      time.Duration // sem aviso de commit (eventos de outra cópia do serviço): 1s
	BatchSize         int           // 100
	LockRetryInterval time.Duration // instância sem o lock tenta assumir a cada: 5s
	Retention         time.Duration // histórico publicado fica por: 7 dias
}

func (o Options) withDefaults() Options {
	if o.PollInterval <= 0 {
		o.PollInterval = time.Second
	}
	if o.BatchSize <= 0 {
		o.BatchSize = 100
	}
	if o.LockRetryInterval <= 0 {
		o.LockRetryInterval = 5 * time.Second
	}
	if o.Retention <= 0 {
		o.Retention = 7 * 24 * time.Hour
	}
	return o
}

const cleanupInterval = time.Hour

// Relay publica o que foi gravado no outbox. Só a instância com o lock fica ativa; ela envia em ordem, para no
// primeiro erro e tenta de novo com backoff, sem pular a fila.
type Relay struct {
	Store     Store
	Transport Transport
	Lock      Lock
	Signal    *Signal
	Clock     func() time.Time
	Options   Options
	Log       *slog.Logger
	// Observe recebe o tamanho da fila depois de cada passada (métricas); nil quando a instância deixa de ser a ativa.
	Observe func(*Stats)
}

// Run roda até ctx ser cancelado.
func (r *Relay) Run(ctx context.Context) {
	opts := r.Options.withDefaults()
	log := r.Log
	if log == nil {
		log = slog.Default()
	}
	observe := r.Observe
	if observe == nil {
		observe = func(*Stats) {}
	}

	for ctx.Err() == nil {
		lease, err := r.Lock.TryAcquire(ctx)
		if err != nil {
			log.ErrorContext(ctx, "outbox lock failed", "error", err)
		}
		if lease == nil {
			sleep(ctx, opts.LockRetryInterval)
			continue
		}
		log.InfoContext(ctx, "outbox relay is active on this instance")
		if err := r.relayWhileHolding(ctx, lease, opts, log, observe); err != nil && ctx.Err() == nil {
			log.ErrorContext(ctx, "outbox relay failed; retrying", "error", err)
		}
		observe(nil)
		_ = lease.Release()
		if ctx.Err() == nil {
			sleep(ctx, opts.LockRetryInterval)
		}
	}
}

func (r *Relay) relayWhileHolding(ctx context.Context, lease Lease, opts Options, log *slog.Logger, observe func(*Stats)) error {
	failures := 0
	nextCleanup := r.Clock()
	for ctx.Err() == nil && lease.Held(ctx) {
		result, err := ProcessBatch(ctx, r.Store, r.Transport, r.Clock, opts.BatchSize, log)
		if err != nil {
			return err
		}
		if stats, err := r.Store.Stats(ctx); err == nil {
			observe(&stats)
		}
		if r.Clock().After(nextCleanup) {
			if _, err := r.Store.DeletePublishedBefore(ctx, r.Clock().Add(-opts.Retention)); err != nil {
				log.WarnContext(ctx, "outbox cleanup failed", "error", err)
			}
			nextCleanup = r.Clock().Add(cleanupInterval)
		}

		switch {
		case result.Failed:
			failures++
			sleep(ctx, backoff(failures))
		case result.Published < opts.BatchSize:
			failures = 0
			select {
			case <-ctx.Done():
			case <-r.Signal.C():
			case <-time.After(opts.PollInterval):
			}
		}
		// Lote cheio: provavelmente há mais pendentes, segue sem esperar.
	}
	return nil
}

// backoff: 0,5s, 1s, 2s, 4s... até 10s.
func backoff(failures int) time.Duration {
	return min(500*time.Millisecond<<min(failures-1, 5), 10*time.Second)
}

func sleep(ctx context.Context, d time.Duration) {
	select {
	case <-ctx.Done():
	case <-time.After(d):
	}
}
