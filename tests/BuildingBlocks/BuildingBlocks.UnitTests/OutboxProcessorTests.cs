using BuildingBlocks.Outbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace BuildingBlocks.UnitTests;

public class OutboxProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : IOutboxStore
    {
        public List<OutboxMessage> Messages { get; } = [];

        /// <summary>Cada chamada de MarkPublished com as sequências que recebeu.</summary>
        public List<long[]> PublishCalls { get; } = [];

        public OutboxMessage Add(string type = "order.placed")
        {
            var message = OutboxMessage.Create(type, "key", Guid.CreateVersion7(), "{}", traceParent: null, Now);
            message.Sequence = Messages.Count + 1;
            Messages.Add(message);
            return message;
        }

        public Task<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>(
                Messages.Where(m => m.PublishedAt is null).OrderBy(m => m.Sequence).Take(max).ToList());

        public Task MarkPublishedAsync(IReadOnlyCollection<long> sequences, DateTimeOffset publishedAt, CancellationToken cancellationToken)
        {
            PublishCalls.Add([.. sequences]);
            foreach (var message in Messages.Where(m => sequences.Contains(m.Sequence)))
            {
                message.PublishedAt = publishedAt;
            }
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(long sequence, string error, CancellationToken cancellationToken)
        {
            var message = Messages.Single(m => m.Sequence == sequence);
            message.Attempts++;
            message.LastError = error;
            return Task.CompletedTask;
        }

        public Task<OutboxStats> GetStatsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> DeletePublishedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Registra a ordem em que os envios começam. Com <see cref="HoldAcks"/>, o ack de cada um só chega quando o teste
    /// libera (<see cref="ReleaseAcks"/>) — para provar que o lote inteiro sai antes de esperar qualquer ack.
    /// </summary>
    private sealed class FakeTransport : IOutboxTransport
    {
        private readonly List<TaskCompletionSource> _acks = [];

        public List<long> Started { get; } = [];

        public long? FailOn { get; set; }

        public bool HoldAcks { get; set; }

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Started.Add(message.Sequence);
            if (message.Sequence == FailOn)
            {
                return Task.FromException(new InvalidOperationException("broker down"));
            }
            if (!HoldAcks)
            {
                return Task.CompletedTask;
            }
            var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _acks.Add(ack);
            return ack.Task;
        }

        public void ReleaseAcks() => _acks.ForEach(a => a.SetResult());
    }

    private static OutboxProcessor Processor(FakeStore store, FakeTransport transport) =>
        new(store, transport, new FixedClock(Now), NullLogger<OutboxProcessor>.Instance);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Pending_messages_are_sent_in_order_and_marked_published_in_one_go()
    {
        var store = new FakeStore();
        store.Add();
        store.Add();
        store.Add();
        var transport = new FakeTransport();

        var result = await Processor(store, transport).ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        Assert.Equal([1L, 2L, 3L], transport.Started);
        Assert.All(store.Messages, m => Assert.Equal(Now, m.PublishedAt));
        Assert.Equal([1L, 2L, 3L], Assert.Single(store.PublishCalls));
        Assert.Equal(new OutboxBatchResult(Published: 3, Failed: false), result);
    }

    [Fact]
    public async Task The_whole_batch_goes_out_before_any_ack_is_awaited()
    {
        // Esperar o ack de um em um limitava a vazão do relay (fila no Cart no teste de carga da T-027).
        var store = new FakeStore();
        store.Add();
        store.Add();
        store.Add();
        var transport = new FakeTransport { HoldAcks = true };

        var processing = Processor(store, transport).ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        Assert.Equal([1L, 2L, 3L], transport.Started);
        Assert.False(processing.IsCompleted);
        Assert.All(store.Messages, m => Assert.Null(m.PublishedAt));
        transport.ReleaseAcks();
        Assert.Equal(3, (await processing).Published);
    }

    [Fact]
    public async Task A_batch_sends_at_most_the_batch_size()
    {
        var store = new FakeStore();
        store.Add();
        store.Add();
        store.Add();
        var transport = new FakeTransport();

        var result = await Processor(store, transport).ProcessBatchAsync(batchSize: 2, CancellationToken.None);

        Assert.Equal([1L, 2L], transport.Started);
        Assert.Null(store.Messages[2].PublishedAt);
        Assert.Equal(2, result.Published);
    }

    [Fact]
    public async Task Only_the_messages_before_a_failure_count_as_published()
    {
        // O que veio depois da falha fica pendente e é reenviado junto com ela, na ordem: nada passa na frente.
        // Se já tinha chegado ao Kafka, o consumidor recebe de novo e ignora (é idempotente).
        var store = new FakeStore();
        store.Add();
        store.Add();
        store.Add();
        var transport = new FakeTransport { FailOn = 2 };

        var result = await Processor(store, transport).ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        Assert.Equal(new OutboxBatchResult(Published: 1, Failed: true), result);
        Assert.Equal(Now, store.Messages[0].PublishedAt);
        var failed = store.Messages[1];
        Assert.Null(failed.PublishedAt);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal("broker down", failed.LastError);
        Assert.Null(store.Messages[2].PublishedAt);
        Assert.Equal(0, store.Messages[2].Attempts);
    }

    [Fact]
    public async Task Nothing_pending_means_nothing_sent()
    {
        var store = new FakeStore();
        var transport = new FakeTransport();

        var result = await Processor(store, transport).ProcessBatchAsync(batchSize: 10, CancellationToken.None);

        Assert.Empty(transport.Started);
        Assert.Empty(store.PublishCalls);
        Assert.Equal(new OutboxBatchResult(Published: 0, Failed: false), result);
    }
}
