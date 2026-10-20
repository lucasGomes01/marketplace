// Package outbox grava eventos na mesma transação da mudança e os publica depois (ADR-005). O store grava as
// mensagens junto com o pedido; o Relay as envia ao Kafka em ordem, uma instância ativa por vez (lock no SQL Server).
// Equivalente ao BuildingBlocks.Outbox dos serviços .NET.
package outbox

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"time"

	"go.opentelemetry.io/otel/propagation"
)

// Message é uma linha da tabela OutboxMessages.
type Message struct {
	Sequence    int64 // ordem de gravação; o relay publica nesta ordem
	EventID     string
	Type        string // = tópico
	Key         string
	Payload     []byte // envelope JSON pronto
	TraceParent string // W3C traceparent de quem gravou ("" = sem trace)
	CreatedAt   time.Time
	PublishedAt time.Time // zero = pendente
	Attempts    int
}

// Stats é o tamanho da fila; OldestPendingSince zero = nada pendente.
type Stats struct {
	Pending            int
	OldestPendingSince time.Time
}

// Store é o acesso do relay à tabela.
type Store interface {
	FetchPending(ctx context.Context, max int) ([]Message, error)
	// MarkPublished marca um lote de uma vez (um UPDATE só).
	MarkPublished(ctx context.Context, sequences []int64, at time.Time) error
	// MarkFailed soma uma tentativa e guarda o motivo; a mensagem continua pendente.
	MarkFailed(ctx context.Context, sequence int64, reason string) error
	Stats(ctx context.Context) (Stats, error)
	DeletePublishedBefore(ctx context.Context, before time.Time) (int64, error)
}

// Transport envia um lote ao broker: dispara todos em ordem e só então espera as confirmações. Devolve um erro por
// mensagem (nil = confirmada), na mesma ordem do lote.
type Transport interface {
	SendBatch(ctx context.Context, batch []Message) []error
}

// Lock garante um relay ativo por serviço, mesmo com várias cópias rodando (preserva a ordem).
type Lock interface {
	// TryAcquire devolve nil (sem erro) se outra instância já tem o lock.
	TryAcquire(ctx context.Context) (Lease, error)
}

// Lease é o lock obtido.
type Lease interface {
	// Held é false se o lock se perdeu (ex.: a conexão caiu).
	Held(ctx context.Context) bool
	Release() error
}

// Result de uma passada do relay.
type Result struct {
	Published int
	Failed    bool
}

// ProcessBatch envia os pendentes como um lote (o produtor idempotente mantém a ordem por partição com vários envios
// em voo; esperar um ack por vez limitava a vazão, T-027). Só conta como publicado o trecho antes do primeiro erro: o
// resto fica pendente e é reenviado em ordem, para que uma mensagem posterior (ex.: Packed) nunca fique publicada sem
// a anterior (Picked). O erro devolvido é só de acesso ao banco.
func ProcessBatch(ctx context.Context, store Store, transport Transport, clock func() time.Time, batchSize int, log *slog.Logger) (Result, error) {
	if log == nil {
		log = slog.Default()
	}
	pending, err := store.FetchPending(ctx, batchSize)
	if err != nil {
		return Result{}, fmt.Errorf("fetch pending: %w", err)
	}
	if len(pending) == 0 {
		return Result{}, nil
	}

	errs := transport.SendBatch(ctx, pending)
	published := len(pending)
	for i, err := range errs {
		if err != nil {
			published = i
			break
		}
	}
	if published > 0 {
		sequences := make([]int64, published)
		for i := range sequences {
			sequences[i] = pending[i].Sequence
		}
		if err := store.MarkPublished(ctx, sequences, clock()); err != nil {
			return Result{}, fmt.Errorf("mark published: %w", err)
		}
	}
	if published == len(pending) {
		return Result{Published: published}, nil
	}

	failed := pending[published]
	log.WarnContext(ctx, "outbox could not send", "type", failed.Type, "eventId", failed.EventID, "attempt", failed.Attempts+1, "error", errs[published])
	return Result{Published: published, Failed: true}, store.MarkFailed(ctx, failed.Sequence, errs[published].Error())
}

// Signal é o aviso em memória "tem evento novo": acorda o relay logo depois do commit. Avisos repetidos antes de o
// relay acordar viram um só.
type Signal struct{ ch chan struct{} }

func NewSignal() *Signal { return &Signal{ch: make(chan struct{}, 1)} }

// Notify nunca bloqueia.
func (s *Signal) Notify() {
	select {
	case s.ch <- struct{}{}:
	default:
	}
}

func (s *Signal) C() <-chan struct{} { return s.ch }

// Envelope serializa o evento no formato comum a todos os serviços: { eventId, type, occurredAt, data }.
func Envelope(eventType, eventID string, occurredAt time.Time, data any) ([]byte, error) {
	payload, err := json.Marshal(struct {
		EventID    string    `json:"eventId"`
		Type       string    `json:"type"`
		OccurredAt time.Time `json:"occurredAt"`
		Data       any       `json:"data"`
	}{eventID, eventType, occurredAt.UTC(), data})
	if err != nil {
		return nil, fmt.Errorf("encode %s: %w", eventType, err)
	}
	return payload, nil
}

var traceContext = propagation.TraceContext{}

// TraceParent é o traceparent (W3C) do span em ctx, para gravar junto com o evento ("" se não há span).
func TraceParent(ctx context.Context) string {
	carrier := propagation.MapCarrier{}
	traceContext.Inject(ctx, carrier)
	return carrier.Get("traceparent")
}

// ContextWithTraceParent devolve um contexto cujo span pai é o traceparent gravado: a publicação, feita depois e
// fora da requisição, continua o trace de quem registrou o evento.
func ContextWithTraceParent(ctx context.Context, traceparent string) context.Context {
	if traceparent == "" {
		return ctx
	}
	return traceContext.Extract(ctx, propagation.MapCarrier{"traceparent": traceparent})
}
