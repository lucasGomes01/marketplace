using BuildingBlocks.Application.Messaging;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.Kafka;
using Testcontainers.MsSql;

namespace BuildingBlocks.Outbox.IntegrationTests;

/// <summary>Dado de negócio de mentira: o outbox grava o evento junto com ele.</summary>
public sealed class Note
{
    public Guid Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>().HasKey(n => n.Id);
        modelBuilder.AddOutbox();
    }
}

public sealed record NoteCreated(Guid NoteId, string Text)
{
    public const string EventType = "note.created";
}

/// <summary>Um SQL Server e um Kafka para a classe toda; cada teste ganha um banco próprio.</summary>
public sealed class OutboxFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.1.0")
        .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
        .Build();

    public string KafkaBootstrapServers => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sqlServer.StartAsync(), _kafka.StartAsync());
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = KafkaBootstrapServers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = NoteCreated.EventType, NumPartitions = 3, ReplicationFactor = 1 }]);
    }

    public async Task DisposeAsync()
    {
        await _sqlServer.DisposeAsync();
        await _kafka.DisposeAsync();
    }

    /// <summary>Banco novo com a tabela de notas e a do outbox.</summary>
    public async Task<string> NewDatabaseAsync()
    {
        var connectionString = new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
        {
            InitialCatalog = $"outbox_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new NotesDbContext(new DbContextOptionsBuilder<NotesDbContext>().UseSqlServer(connectionString).Options);
        await db.Database.EnsureCreatedAsync();
        return connectionString;
    }

    /// <summary>
    /// Serviço como os de verdade montam: DbContext + <c>AddOutbox</c>. <paramref name="kafka"/> permite apontar
    /// para um broker que não existe.
    /// </summary>
    public ServiceProvider CreateServices(string connectionString, string? kafka = null, TimeSpan? pollInterval = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = kafka ?? KafkaBootstrapServers,
            ["Kafka:MessageTimeoutMs"] = "2000",
            ["Outbox:PollInterval"] = (pollInterval ?? TimeSpan.FromMilliseconds(200)).ToString(),
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddDebug());
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<NotesDbContext>(options => options.UseSqlServer(connectionString));
        services.AddOutbox<NotesDbContext>(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Grava uma nota e o evento dela numa só transação, como um handler faria. <paramref name="key"/> é a chave no
    /// Kafka (padrão: id da nota); a ordem só é garantida entre mensagens da mesma chave.
    /// </summary>
    public static async Task<Note> CreateNoteAsync(IServiceProvider services, string text = "olá", string? key = null)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var note = new Note { Id = Guid.CreateVersion7(), Text = text };
        db.Notes.Add(note);
        await events.PublishAsync(NoteCreated.EventType, key ?? note.Id.ToString(), note.Id, new NoteCreated(note.Id, text), CancellationToken.None);
        await db.SaveChangesAsync();
        return note;
    }

    public static async Task<List<OutboxMessage>> OutboxAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotesDbContext>()
            .Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.Sequence).ToListAsync();
    }

    public static IHostedService Relay(IServiceProvider services) =>
        services.GetServices<IHostedService>().Single(s => s.GetType().Name.StartsWith("OutboxRelay"));

    /// <summary>Lê o tópico desde o início até achar as mensagens com os ids pedidos (ou desistir).</summary>
    public List<Message<string, string>> Consume(string topic, IReadOnlyCollection<Guid> eventIds, TimeSpan? timeout = null)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = KafkaBootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        var wanted = eventIds.Select(id => id.ToString()).ToHashSet();
        var found = new List<Message<string, string>>();
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (found.Count < wanted.Count && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message is { } message &&
                wanted.Contains(System.Text.Encoding.UTF8.GetString(message.Headers.GetLastBytes("event-id"))))
            {
                found.Add(message);
            }
        }
        return found;
    }
}
