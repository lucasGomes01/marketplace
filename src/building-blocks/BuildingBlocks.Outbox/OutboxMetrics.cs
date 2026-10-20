using System.Diagnostics.Metrics;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Saúde do outbox para o painel (fonte "Marketplace", exportada pelo OpenTelemetry quando ligado). Só a instância
/// com o relay ativo informa: as outras não sabem o estado da fila.
/// </summary>
internal static class OutboxMetrics
{
    private static readonly Meter Meter = new("Marketplace");

    private static OutboxStats? _last;
    private static DateTimeOffset _measuredAt;

    static OutboxMetrics()
    {
        Meter.CreateObservableGauge("marketplace.outbox.pending", ObservePending,
            description: "Eventos gravados no outbox que ainda não foram publicados no Kafka.");
        Meter.CreateObservableGauge("marketplace.outbox.oldest_age", ObserveOldestAge, unit: "s",
            description: "Há quanto tempo o evento pendente mais antigo espera (0 = nada pendente).");
    }

    public static void Record(OutboxStats stats, DateTimeOffset now)
    {
        _last = stats;
        _measuredAt = now;
    }

    /// <summary>A instância deixou de ser a ativa (ou parou): para de informar.</summary>
    public static void Clear() => _last = null;

    private static IEnumerable<Measurement<int>> ObservePending() =>
        _last is { } s ? [new Measurement<int>(s.Pending)] : [];

    private static IEnumerable<Measurement<double>> ObserveOldestAge() =>
        _last is { } s
            ? [new Measurement<double>(s.OldestPendingSince is { } since ? Math.Max(0, (_measuredAt - since).TotalSeconds) : 0)]
            : [];
}
