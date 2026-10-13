package messaging

import (
	"context"
	"encoding/json"
	"errors"
	"sync"
	"testing"
	"time"

	"github.com/testcontainers/testcontainers-go/modules/kafka"
	"github.com/twmb/franz-go/pkg/kadm"
	"github.com/twmb/franz-go/pkg/kgo"
)

// startKafka sobe um Kafka real com os tópicos já criados (como o kafka-init do compose). Pulado com -short.
func startKafka(t *testing.T, topics ...string) []string {
	t.Helper()
	if testing.Short() {
		t.Skip("integração: precisa de Docker (rode sem -short)")
	}
	ctx := context.Background()
	container, err := kafka.Run(ctx, "confluentinc/confluent-local:7.5.0") // versão suportada pelo módulo testcontainers-go v0.44
	if err != nil {
		t.Fatalf("start kafka: %v", err)
	}
	t.Cleanup(func() { _ = container.Terminate(context.Background()) })

	brokers, err := container.Brokers(ctx)
	if err != nil {
		t.Fatal(err)
	}
	client, err := kgo.NewClient(kgo.SeedBrokers(brokers...))
	if err != nil {
		t.Fatal(err)
	}
	defer client.Close()
	if _, err := kadm.NewClient(client).CreateTopics(ctx, 3, 1, nil, topics...); err != nil {
		t.Fatalf("create topics: %v", err)
	}
	return brokers
}

func produceRaw(t *testing.T, brokers []string, topic, key, value string) {
	t.Helper()
	client, err := kgo.NewClient(kgo.SeedBrokers(brokers...))
	if err != nil {
		t.Fatal(err)
	}
	defer client.Close()
	if err := client.ProduceSync(context.Background(), &kgo.Record{Topic: topic, Key: []byte(key), Value: []byte(value)}).FirstErr(); err != nil {
		t.Fatalf("produce: %v", err)
	}
}

func envelopeJSON(eventID string, data any) string {
	b, _ := json.Marshal(map[string]any{"eventId": eventID, "type": "cart.checked-out", "occurredAt": "2026-10-13T12:00:00Z", "data": data})
	return string(b)
}

type recorder struct {
	mu   sync.Mutex
	seen []Envelope
}

func (r *recorder) handle(_ context.Context, env Envelope) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.seen = append(r.seen, env)
	return nil
}

func (r *recorder) ids() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := make([]string, len(r.seen))
	for i, e := range r.seen {
		out[i] = e.EventID
	}
	return out
}

func waitFor(t *testing.T, timeout time.Duration, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if condition() {
			return
		}
		time.Sleep(100 * time.Millisecond)
	}
	t.Fatal("condition not met in time")
}

func runConsumer(t *testing.T, brokers []string, group string, handler Handler) context.CancelFunc {
	t.Helper()
	consumer, err := NewConsumer(brokers, group, []string{"cart.checked-out"}, handler, nil)
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	go func() { defer close(done); _ = consumer.Run(ctx) }()
	return func() { cancel(); <-done; consumer.Close() }
}

func TestKafkaWithRealBroker(t *testing.T) {
	brokers := startKafka(t, "cart.checked-out", "order.placed")

	t.Run("publisher writes the standard envelope with key and headers", func(t *testing.T) {
		client, err := kgo.NewClient(kgo.SeedBrokers(brokers...))
		if err != nil {
			t.Fatal(err)
		}
		pub := NewPublisher(client, func() time.Time { return time.Date(2026, 10, 13, 12, 0, 0, 0, time.UTC) })
		defer pub.Close()

		if err := pub.Publish(context.Background(), "order.placed", "order-1", "order-1", map[string]any{"orderId": "order-1"}); err != nil {
			t.Fatalf("publish: %v", err)
		}

		reader, _ := kgo.NewClient(kgo.SeedBrokers(brokers...), kgo.ConsumeTopics("order.placed"), kgo.ConsumeResetOffset(kgo.NewOffset().AtStart()))
		defer reader.Close()
		ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer cancel()
		record := reader.PollRecords(ctx, 1).Records()
		if len(record) != 1 {
			t.Fatal("no record in order.placed")
		}
		headers := map[string]string{}
		for _, h := range record[0].Headers {
			headers[h.Key] = string(h.Value)
		}
		var env map[string]any
		_ = json.Unmarshal(record[0].Value, &env)
		if string(record[0].Key) != "order-1" || headers["event-type"] != "order.placed" || headers["event-id"] != "order-1" {
			t.Errorf("key = %q, headers = %v", record[0].Key, headers)
		}
		if env["eventId"] != "order-1" || env["type"] != "order.placed" || env["occurredAt"] != "2026-10-13T12:00:00Z" ||
			env["data"].(map[string]any)["orderId"] != "order-1" {
			t.Errorf("envelope = %v", env)
		}
	})

	t.Run("consumer delivers events, skips poison messages and commits offsets", func(t *testing.T) {
		produceRaw(t, brokers, "cart.checked-out", "cart-1", envelopeJSON("evt-1", map[string]any{"cartId": "cart-1"}))
		produceRaw(t, brokers, "cart.checked-out", "cart-1", "{ not json")
		produceRaw(t, brokers, "cart.checked-out", "cart-1", envelopeJSON("evt-permanent", map[string]any{}))
		produceRaw(t, brokers, "cart.checked-out", "cart-1", envelopeJSON("evt-2", map[string]any{"cartId": "cart-1"}))

		rec := &recorder{}
		handler := func(ctx context.Context, env Envelope) error {
			if env.EventID == "evt-permanent" {
				return Permanent(errors.New("invalid order"))
			}
			return rec.handle(ctx, env)
		}
		stop := runConsumer(t, brokers, "orders-test", handler)
		waitFor(t, 30*time.Second, func() bool { return len(rec.ids()) == 2 })
		stop()

		if got := rec.ids(); got[0] != "evt-1" || got[1] != "evt-2" {
			t.Errorf("handled %v, want [evt-1 evt-2] in order", got)
		}
		var data map[string]string
		_ = json.Unmarshal(rec.seen[0].Data, &data)
		if rec.seen[0].Type != "cart.checked-out" || data["cartId"] != "cart-1" {
			t.Errorf("envelope = %+v, data = %v", rec.seen[0], data)
		}

		// Mesmo grupo de novo: os offsets foram confirmados, nada é reentregue.
		again := &recorder{}
		stop = runConsumer(t, brokers, "orders-test", again.handle)
		time.Sleep(5 * time.Second)
		stop()
		if len(again.ids()) != 0 {
			t.Errorf("redelivered %v after commit", again.ids())
		}
	})

	t.Run("a temporary failure is retried until it succeeds", func(t *testing.T) {
		produceRaw(t, brokers, "cart.checked-out", "cart-9", envelopeJSON("evt-retry", map[string]any{}))
		attempts := 0
		var mu sync.Mutex
		handler := func(context.Context, Envelope) error {
			mu.Lock()
			defer mu.Unlock()
			attempts++
			if attempts < 3 {
				return errors.New("database down")
			}
			return nil
		}

		stop := runConsumer(t, brokers, "orders-retry-test", func(ctx context.Context, env Envelope) error {
			if env.EventID != "evt-retry" {
				return nil
			}
			return handler(ctx, env)
		})
		waitFor(t, 30*time.Second, func() bool { mu.Lock(); defer mu.Unlock(); return attempts >= 3 })
		stop()
	})
}
