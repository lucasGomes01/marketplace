// Package messaging é o adaptador Kafka (franz-go) do Orders: publicador e consumidor com o mesmo envelope
// dos serviços .NET (BuildingBlocks.Messaging). Contratos em .ai/context/events.md.
package messaging

import (
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"sync"
	"time"

	"github.com/twmb/franz-go/pkg/kgo"
	"github.com/twmb/franz-go/plugin/kotel"
	"go.opentelemetry.io/otel/trace"

	"marketplace/orders/internal/outbox"
)

// Envelope de todo evento: { eventId, type, occurredAt, data }.
type Envelope struct {
	EventID    string          `json:"eventId"`
	Type       string          `json:"type"`
	OccurredAt time.Time       `json:"occurredAt"`
	Data       json.RawMessage `json:"data"`
}

const (
	headerEventType = "event-type"
	headerEventID   = "event-id"
)

// tracer cria os spans de envio/consumo e grava/lê o traceparent (W3C) nos headers — o mesmo formato do
// BuildingBlocks.Messaging (.NET), então um trace atravessa os serviços. Usa os provedores globais do OpenTelemetry
// (no-op sem telemetria ligada).
func tracer() *kotel.Tracer { return kotel.NewTracer() }

// Tracing é a opção do cliente franz-go que liga os hooks de trace (use também no cliente do Publisher).
func Tracing() kgo.Opt { return kgo.WithHooks(kotel.NewKotel(kotel.WithTracer(tracer())).Hooks()...) }

// Publisher é o transporte do relay do outbox: envia ao tópico de mesmo nome do tipo o envelope já gravado.
type Publisher struct {
	client *kgo.Client
}

var _ outbox.Transport = (*Publisher)(nil)

func NewPublisher(client *kgo.Client) *Publisher { return &Publisher{client: client} }

// SendBatch dispara o lote inteiro em ordem (Produce é assíncrono) e espera todas as confirmações do broker (acks de
// todas as réplicas, produtor idempotente — padrão do franz-go, que mantém a ordem por partição). O span de envio de
// cada mensagem é filho do traceparent gravado com o evento: o trace da requisição continua mesmo publicando depois.
func (p *Publisher) SendBatch(ctx context.Context, batch []outbox.Message) []error {
	errs := make([]error, len(batch))
	var acks sync.WaitGroup
	acks.Add(len(batch))
	for i, m := range batch {
		record := &kgo.Record{
			Topic: m.Type,
			Key:   []byte(m.Key),
			Value: m.Payload,
			Headers: []kgo.RecordHeader{
				{Key: headerEventType, Value: []byte(m.Type)},
				{Key: headerEventID, Value: []byte(m.EventID)},
			},
		}
		p.client.Produce(outbox.ContextWithTraceParent(ctx, m.TraceParent), record, func(_ *kgo.Record, err error) {
			errs[i] = err
			acks.Done()
		})
	}
	acks.Wait()
	return errs
}

func (p *Publisher) Close() { p.client.Close() }

// Handler processa um evento. Erro comum → tenta de novo; Permanent(err) → registra e pula.
type Handler func(ctx context.Context, env Envelope) error

type permanentError struct{ err error }

func (e permanentError) Error() string { return e.err.Error() }
func (e permanentError) Unwrap() error { return e.err }

// Permanent marca um erro que nunca vai se resolver tentando de novo (dado inválido).
func Permanent(err error) error { return permanentError{err} }

// IsPermanent diz se o erro foi marcado com Permanent.
func IsPermanent(err error) bool { return errors.As(err, new(permanentError)) }

// Consumer lê tópicos num consumer group com entrega at-least-once: o offset só é confirmado depois que o
// handler terminou (com sucesso ou com erro permanente). Mensagens de uma partição são processadas em ordem.
type Consumer struct {
	client *kgo.Client
	tracer *kotel.Tracer
	handle Handler
	log    *slog.Logger
}

func NewConsumer(brokers []string, group string, topics []string, handle Handler, log *slog.Logger) (*Consumer, error) {
	if log == nil {
		log = slog.Default()
	}
	trace := tracer()
	client, err := kgo.NewClient(
		kgo.WithHooks(kotel.NewKotel(kotel.WithTracer(trace)).Hooks()...),
		kgo.SeedBrokers(brokers...),
		kgo.ConsumerGroup(group),
		kgo.ConsumeTopics(topics...),
		kgo.DisableAutoCommit(),
		kgo.ConsumeResetOffset(kgo.NewOffset().AtStart()),
	)
	if err != nil {
		return nil, err
	}
	return &Consumer{client: client, tracer: trace, handle: handle, log: log}, nil
}

// Run consome até o contexto ser cancelado.
func (c *Consumer) Run(ctx context.Context) error {
	for {
		fetches := c.client.PollFetches(ctx)
		if ctx.Err() != nil {
			return nil
		}
		fetches.EachError(func(topic string, partition int32, err error) {
			c.log.Error("kafka fetch error", "topic", topic, "partition", partition, "error", err)
		})

		for _, record := range fetches.Records() {
			if !c.process(ctx, record) {
				return nil // contexto cancelado no meio: não confirma, a mensagem volta depois
			}
			c.commit(ctx, record)
		}
	}
}

// commit confirma o offset de uma mensagem já processada. Usa um contexto próprio: se o serviço estiver
// desligando (ctx cancelado), a confirmação ainda acontece — senão a mensagem seria reprocessada no próximo início.
func (c *Consumer) commit(ctx context.Context, record *kgo.Record) {
	commitCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), 5*time.Second)
	defer cancel()
	if err := c.client.CommitRecords(commitCtx, record); err != nil {
		c.log.Error("kafka commit failed", "topic", record.Topic, "partition", record.Partition, "offset", record.Offset, "error", err)
	}
}

// process devolve false só se o contexto foi cancelado antes de concluir.
func (c *Consumer) process(ctx context.Context, record *kgo.Record) bool {
	// Span de processamento filho do span de quem publicou; banco e publicações do handler ficam dentro dele.
	_, span := c.tracer.WithProcessSpan(record)
	defer span.End()
	ctx = trace.ContextWithSpan(ctx, span) // mantém o cancelamento do consumidor

	var env Envelope
	if err := json.Unmarshal(record.Value, &env); err != nil {
		c.log.Error("skipping malformed message", "topic", record.Topic, "offset", record.Offset, "error", err)
		return true
	}

	for attempt := 0; ; attempt++ {
		err := c.handle(ctx, env)
		switch {
		case err == nil:
			return true
		case IsPermanent(err):
			c.log.Error("skipping invalid event", "type", env.Type, "eventId", env.EventID, "error", err)
			return true
		}

		wait := backoff(attempt)
		c.log.Warn("event failed, retrying", "type", env.Type, "eventId", env.EventID, "attempt", attempt+1, "in", wait, "error", err)
		select {
		case <-ctx.Done():
			return false
		case <-time.After(wait):
		}
	}
}

// backoff: 0,5s, 1s, 2s, 4s... até 10s.
func backoff(attempt int) time.Duration {
	wait := 500 * time.Millisecond << min(attempt, 5)
	return min(wait, 10*time.Second)
}

// Close sai do consumer group.
func (c *Consumer) Close() { c.client.Close() }
