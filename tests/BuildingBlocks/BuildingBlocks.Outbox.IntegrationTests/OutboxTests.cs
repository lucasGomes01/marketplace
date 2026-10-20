using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Outbox.IntegrationTests;

public class OutboxTests(OutboxFixture fixture) : IClassFixture<OutboxFixture>
{
    [Fact]
    public async Task The_event_is_saved_together_with_the_business_change_and_not_sent_to_kafka()
    {
        await using var services = fixture.CreateServices(await fixture.NewDatabaseAsync());
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "outbox-test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("outbox-test");
        using var request = source.StartActivity("request")!;

        var note = await OutboxFixture.CreateNoteAsync(services, "primeira");

        var message = Assert.Single(await OutboxFixture.OutboxAsync(services));
        Assert.Equal(note.Id, message.EventId);
        Assert.Equal(NoteCreated.EventType, message.Type);
        Assert.Equal(note.Id.ToString(), message.Key);
        Assert.Null(message.PublishedAt);
        using var envelope = JsonDocument.Parse(message.Payload);
        Assert.Equal(note.Id, envelope.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal("primeira", envelope.RootElement.GetProperty("data").GetProperty("text").GetString());
        Assert.Equal(request.Id, message.TraceParent);
        // O relay não foi iniciado: nada pode ter ido ao Kafka.
        Assert.Empty(fixture.Consume(NoteCreated.EventType, [note.Id], TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task When_the_transaction_fails_neither_the_change_nor_the_event_is_saved()
    {
        await using var services = fixture.CreateServices(await fixture.NewDatabaseAsync());
        var existing = await OutboxFixture.CreateNoteAsync(services);

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            db.Notes.Add(new Note { Id = existing.Id, Text = "chave repetida" }); // viola a PK no SaveChanges
            var eventId = Guid.CreateVersion7();
            await events.PublishAsync(NoteCreated.EventType, "k", eventId, new NoteCreated(existing.Id, "x"), CancellationToken.None);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        var message = Assert.Single(await OutboxFixture.OutboxAsync(services));
        Assert.Equal(existing.Id, message.EventId);
    }

    [Fact]
    public async Task The_same_event_is_recorded_only_once()
    {
        await using var services = fixture.CreateServices(await fixture.NewDatabaseAsync());
        var note = await OutboxFixture.CreateNoteAsync(services);

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            await events.PublishAsync(NoteCreated.EventType, "k", note.Id, new NoteCreated(note.Id, "de novo"), CancellationToken.None);
            await events.PublishAsync(NoteCreated.EventType, "k", note.Id, new NoteCreated(note.Id, "de novo"), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        Assert.Single(await OutboxFixture.OutboxAsync(services));
    }

    [Fact]
    public async Task The_relay_publishes_pending_events_in_order_and_marks_them_published()
    {
        await using var services = fixture.CreateServices(await fixture.NewDatabaseAsync());
        // Mesma chave = mesmo agregado = mesma partição: é aí que a ordem importa (Picked antes de Packed).
        var aggregate = Guid.CreateVersion7().ToString();
        var notes = new List<Note>();
        for (var i = 1; i <= 3; i++)
        {
            notes.Add(await OutboxFixture.CreateNoteAsync(services, $"nota {i}", key: aggregate));
        }
        var relay = OutboxFixture.Relay(services);

        await relay.StartAsync(CancellationToken.None);
        var messages = fixture.Consume(NoteCreated.EventType, notes.Select(n => n.Id).ToList());
        await relay.StopAsync(CancellationToken.None);

        Assert.Equal(notes.Select(n => n.Id.ToString()), messages.Select(m => Encoding.UTF8.GetString(m.Headers.GetLastBytes("event-id"))));
        Assert.All(messages, m => Assert.Equal(aggregate, m.Key));
        using var envelope = JsonDocument.Parse(messages[0].Value);
        Assert.Equal("nota 1", envelope.RootElement.GetProperty("data").GetProperty("text").GetString());
        await WaitUntilAsync(async () => (await OutboxFixture.OutboxAsync(services)).All(m => m.PublishedAt is not null));
    }

    [Fact]
    public async Task An_event_recorded_while_the_relay_runs_goes_out_without_waiting_for_the_poll()
    {
        // Intervalo de polling enorme: só o aviso do commit pode acordar o relay.
        await using var services = fixture.CreateServices(await fixture.NewDatabaseAsync(), pollInterval: TimeSpan.FromMinutes(5));
        var relay = OutboxFixture.Relay(services);
        await relay.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2)); // relay pegou o lock e está esperando

        var note = await OutboxFixture.CreateNoteAsync(services);

        var messages = fixture.Consume(NoteCreated.EventType, [note.Id], TimeSpan.FromSeconds(10));
        await relay.StopAsync(CancellationToken.None);
        Assert.Single(messages);
    }

    [Fact]
    public async Task With_kafka_down_the_event_stays_pending_and_the_attempts_are_counted()
    {
        await using var services = fixture.CreateServices(await fixture.NewDatabaseAsync(), kafka: "localhost:1");
        await OutboxFixture.CreateNoteAsync(services);
        var relay = OutboxFixture.Relay(services);

        await relay.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await OutboxFixture.OutboxAsync(services)).Single().Attempts >= 1);
        await relay.StopAsync(CancellationToken.None);

        var message = Assert.Single(await OutboxFixture.OutboxAsync(services));
        Assert.Null(message.PublishedAt);
        Assert.False(string.IsNullOrEmpty(message.LastError));
    }

    [Fact]
    public async Task Only_one_relay_per_database_holds_the_lock_at_a_time()
    {
        var connectionString = await fixture.NewDatabaseAsync();
        var first = new SqlServerOutboxLock(connectionString, "outbox:notes");
        var second = new SqlServerOutboxLock(connectionString, "outbox:notes");

        var lease = await first.TryAcquireAsync(CancellationToken.None);
        var blocked = await second.TryAcquireAsync(CancellationToken.None);

        Assert.NotNull(lease);
        Assert.True(await lease.IsHeldAsync(CancellationToken.None));
        Assert.Null(blocked);

        await lease.DisposeAsync();
        await using var takenOver = await second.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(takenOver);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met in 30 s.");
            await Task.Delay(200);
        }
    }
}
