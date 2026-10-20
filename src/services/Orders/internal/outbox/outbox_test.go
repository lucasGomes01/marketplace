package outbox

import (
	"context"
	"encoding/json"
	"errors"
	"testing"
	"time"

	"go.opentelemetry.io/otel/trace"
)

var now = time.Date(2026, 10, 20, 12, 0, 0, 0, time.UTC)

type fakeStore struct {
	messages     []*Message
	failures     map[int64]string
	publishCalls [][]int64
}

func (s *fakeStore) add(eventID string) {
	s.messages = append(s.messages, &Message{Sequence: int64(len(s.messages) + 1), EventID: eventID, Type: "order.placed", Key: "k"})
}

func (s *fakeStore) FetchPending(_ context.Context, max int) ([]Message, error) {
	var pending []Message
	for _, m := range s.messages {
		if m.PublishedAt.IsZero() && len(pending) < max {
			pending = append(pending, *m)
		}
	}
	return pending, nil
}

func (s *fakeStore) MarkPublished(_ context.Context, sequences []int64, at time.Time) error {
	s.publishCalls = append(s.publishCalls, sequences)
	for _, seq := range sequences {
		s.messages[seq-1].PublishedAt = at
	}
	return nil
}

func (s *fakeStore) MarkFailed(_ context.Context, sequence int64, reason string) error {
	s.messages[sequence-1].Attempts++
	if s.failures == nil {
		s.failures = map[int64]string{}
	}
	s.failures[sequence] = reason
	return nil
}

func (s *fakeStore) Stats(context.Context) (Stats, error)                            { return Stats{}, nil }
func (s *fakeStore) DeletePublishedBefore(context.Context, time.Time) (int64, error) { return 0, nil }

// fakeTransport recebe o lote inteiro de uma vez (como o produtor, que dispara tudo e depois espera os acks).
type fakeTransport struct {
	batches [][]string
	sent    []string
	failOn  string
}

func (t *fakeTransport) SendBatch(_ context.Context, batch []Message) []error {
	errs := make([]error, len(batch))
	var ids []string
	for i, m := range batch {
		ids = append(ids, m.EventID)
		if m.EventID == t.failOn {
			errs[i] = errors.New("broker down")
			continue
		}
		t.sent = append(t.sent, m.EventID)
	}
	t.batches = append(t.batches, ids)
	return errs
}

func process(store *fakeStore, transport *fakeTransport, batch int) (Result, error) {
	return ProcessBatch(context.Background(), store, transport, func() time.Time { return now }, batch, nil)
}

func TestPendingMessagesGoOutAsOneBatchInOrderAndAreMarkedPublishedTogether(t *testing.T) {
	store, transport := &fakeStore{}, &fakeTransport{}
	store.add("a")
	store.add("b")
	store.add("c")

	result, err := process(store, transport, 10)

	if err != nil {
		t.Fatal(err)
	}
	if len(transport.batches) != 1 || len(transport.batches[0]) != 3 || transport.batches[0][0] != "a" || transport.batches[0][2] != "c" {
		t.Errorf("batches %v, want one batch [a b c]", transport.batches)
	}
	if len(store.publishCalls) != 1 || len(store.publishCalls[0]) != 3 {
		t.Errorf("MarkPublished calls %v, want one with 3 sequences", store.publishCalls)
	}
	for _, m := range store.messages {
		if !m.PublishedAt.Equal(now) {
			t.Errorf("%s published at %v", m.EventID, m.PublishedAt)
		}
	}
	if result != (Result{Published: 3}) {
		t.Errorf("result = %+v", result)
	}
}

func TestABatchSendsAtMostTheBatchSize(t *testing.T) {
	store, transport := &fakeStore{}, &fakeTransport{}
	store.add("a")
	store.add("b")
	store.add("c")

	result, _ := process(store, transport, 2)

	if len(transport.sent) != 2 || result.Published != 2 || !store.messages[2].PublishedAt.IsZero() {
		t.Errorf("sent %v, result %+v", transport.sent, result)
	}
}

func TestOnlyTheMessagesBeforeAFailureCountAsPublished(t *testing.T) {
	// O que veio depois da falha fica pendente e é reenviado junto com ela, na ordem: nada passa na frente.
	// Se já tinha chegado ao Kafka, o consumidor recebe de novo e ignora (é idempotente).
	store, transport := &fakeStore{}, &fakeTransport{failOn: "b"}
	store.add("a")
	store.add("b")
	store.add("c")

	result, err := process(store, transport, 10)

	if err != nil {
		t.Fatal(err)
	}
	if result != (Result{Published: 1, Failed: true}) {
		t.Errorf("result = %+v", result)
	}
	failed := store.messages[1]
	if !failed.PublishedAt.IsZero() || failed.Attempts != 1 || store.failures[2] != "broker down" {
		t.Errorf("failed message = %+v, reason %q", failed, store.failures[2])
	}
	if store.messages[0].PublishedAt.IsZero() {
		t.Error("the message before the failure should be published")
	}
	if !store.messages[2].PublishedAt.IsZero() || store.messages[2].Attempts != 0 {
		t.Errorf("a later message was marked past the failure: %+v", store.messages[2])
	}
}

func TestASignalWakesTheWaiterAndRepeatedSignalsCollapse(t *testing.T) {
	signal := NewSignal()

	signal.Notify()
	signal.Notify() // não bloqueia nem acumula

	select {
	case <-signal.C():
	case <-time.After(time.Second):
		t.Fatal("signal did not wake the waiter")
	}
	select {
	case <-signal.C():
		t.Fatal("two notifications should collapse into one")
	default:
	}
}

func TestTheEnvelopeIsTheSameAsTheDotNetServices(t *testing.T) {
	payload, err := Envelope("order.placed", "order-1", now, map[string]string{"orderId": "order-1"})

	if err != nil {
		t.Fatal(err)
	}
	var env map[string]any
	_ = json.Unmarshal(payload, &env)
	if env["eventId"] != "order-1" || env["type"] != "order.placed" || env["occurredAt"] != "2026-10-20T12:00:00Z" ||
		env["data"].(map[string]any)["orderId"] != "order-1" {
		t.Errorf("envelope = %s", payload)
	}
}

func TestTheTraceparentRecordedWithTheEventBecomesTheParentWhenPublishing(t *testing.T) {
	traceID, _ := trace.TraceIDFromHex("0af7651916cd43dd8448eb211c80319c")
	spanID, _ := trace.SpanIDFromHex("b7ad6b7169203331")
	recording := trace.ContextWithSpanContext(context.Background(), trace.NewSpanContext(trace.SpanContextConfig{
		TraceID: traceID, SpanID: spanID, TraceFlags: trace.FlagsSampled,
	}))

	traceparent := TraceParent(recording)
	publishing := trace.SpanContextFromContext(ContextWithTraceParent(context.Background(), traceparent))

	if traceparent != "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" {
		t.Errorf("traceparent = %q", traceparent)
	}
	if publishing.TraceID() != traceID || publishing.SpanID() != spanID {
		t.Errorf("parent = %v", publishing)
	}
	if TraceParent(context.Background()) != "" {
		t.Error("no span should mean no traceparent")
	}
}
