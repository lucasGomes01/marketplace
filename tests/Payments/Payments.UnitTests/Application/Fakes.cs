using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.UnitTests.Application;

/// <summary>Repositórios em memória com a mesma unidade de trabalho (como o DbContext).</summary>
public sealed class InMemoryPaymentsStore : IPayoutRepository, IPayoutCardRepository
{
    private readonly List<Payout> _pending = [];

    public List<Payout> Payouts { get; } = [];
    public List<PayoutCard> Cards { get; } = [];
    public int Saves { get; private set; }

    /// <summary>Simula outra instância gravando o mesmo pedido entre a leitura e a gravação.</summary>
    public bool FailNextSaveAsDuplicate { get; set; }

    public Task<Payout?> FindAsync(Guid orderId, Guid sellerId, CancellationToken cancellationToken) =>
        Task.FromResult(Payouts.SingleOrDefault(p => p.OrderId == orderId && p.SellerId == sellerId));

    public Task<IReadOnlyList<Payout>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payout>>(Payouts.Where(p => p.OrderId == orderId).ToList());

    public Task<IReadOnlyList<Payout>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payout>>(Payouts.Where(p => p.SellerId == sellerId).OrderByDescending(p => p.CreatedAt).ToList());

    public Task<IReadOnlyList<Payout>> ListWaitingForCardAsync(Guid sellerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payout>>(Payouts
            .Where(p => p.SellerId == sellerId && p.Status is PayoutStatus.AwaitingCard or PayoutStatus.Failed).ToList());

    public Task<IReadOnlyList<Payout>> ListAwaitingCardWithCardAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payout>>(Payouts
            .Where(p => p.Status == PayoutStatus.AwaitingCard && Cards.Any(c => c.SellerId == p.SellerId)).Take(limit).ToList());

    public Task<IReadOnlyList<Payout>> ListDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payout>>(Payouts
            .Where(p => p.Status == PayoutStatus.Scheduled && p.ScheduledFor <= now)
            .OrderBy(p => p.ScheduledFor).Take(limit).ToList());

    public void Add(Payout payout) => _pending.Add(payout);

    public Task<PayoutCard?> FindAsync(Guid sellerId, CancellationToken cancellationToken) =>
        Task.FromResult(Cards.SingleOrDefault(c => c.SellerId == sellerId));

    public void Add(PayoutCard card) => Cards.Add(card);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSaveAsDuplicate)
        {
            FailNextSaveAsDuplicate = false;
            _pending.Clear();
            throw new DuplicatePayoutException();
        }
        Payouts.AddRange(_pending);
        _pending.Clear();
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>"Cifra" reversível e visivelmente diferente do número.</summary>
public sealed class FakeCardProtector : ICardProtector
{
    public string Protect(string cardNumber) => "enc:" + new string(cardNumber.Reverse().ToArray());

    public string Unprotect(string protectedCardNumber) => new(protectedCardNumber["enc:".Length..].Reverse().ToArray());
}

public sealed class FakePayoutGateway : IPayoutGateway
{
    public List<PayoutTransfer> Transfers { get; } = [];
    public Func<PayoutTransfer, PayoutTransferResult> Respond { get; set; } = t => PayoutTransferResult.Approve($"ref-{t.Reference}");

    public Task<PayoutTransferResult> SendAsync(PayoutTransfer transfer, CancellationToken cancellationToken)
    {
        Transfers.Add(transfer);
        return Task.FromResult(Respond(transfer));
    }
}

public sealed class FakeExchangeRates : IExchangeRateProvider
{
    public Dictionary<(string, string), decimal> Rates { get; } = new() { [("BRL", "USD")] = 0.1834m };
    public bool Down { get; set; }
    public int Calls { get; private set; }

    public Task<ExchangeRate> GetRateAsync(string from, string to, CancellationToken cancellationToken)
    {
        Calls++;
        if (Down)
        {
            throw new ExchangeRateUnavailableException("down");
        }
        return Rates.TryGetValue((from, to), out var rate)
            ? Task.FromResult(new ExchangeRate(from, to, rate, new DateOnly(2026, 11, 3), "mastercard"))
            : throw new UnsupportedCurrencyException(Rates.Keys.Any(k => k.Item1 == from) ? to : from);
    }
}
