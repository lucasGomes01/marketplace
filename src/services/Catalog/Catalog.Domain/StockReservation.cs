namespace Catalog.Domain;

public enum ReservationOutcome
{
    Reserved = 1,
    Rejected = 2,
}

/// <summary>
/// Resultado da reserva de estoque de um pedido (chave = id do pedido). Gravado na mesma transação da baixa:
/// se o evento chegar de novo, o Catalog sabe que já processou e só republica o resultado.
/// </summary>
public sealed class StockReservation
{
    public Guid OrderId { get; private set; }
    public ReservationOutcome Outcome { get; private set; }
    public string? Reason { get; private set; }
    public List<Guid> RejectedProductIds { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    // Usado pelo EF Core.
    private StockReservation() { }

    public static StockReservation Reserved(Guid orderId, TimeProvider clock) =>
        new() { OrderId = orderId, Outcome = ReservationOutcome.Reserved, CreatedAt = clock.GetUtcNow() };

    public static StockReservation Rejected(Guid orderId, string reason, IEnumerable<Guid> productIds, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection needs a reason.", "reason");
        }
        return new()
        {
            OrderId = orderId,
            Outcome = ReservationOutcome.Rejected,
            Reason = reason.Trim(),
            RejectedProductIds = productIds.Distinct().ToList(),
            CreatedAt = clock.GetUtcNow(),
        };
    }
}
