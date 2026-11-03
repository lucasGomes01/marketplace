using BuildingBlocks.Application.Messaging;
using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.RecordSale;

/// <summary>Evento consumido (publicado pelo Cart). Contrato em <c>.ai/context/events.md</c>.</summary>
public sealed record CartCheckedOut(Guid CheckoutId, Guid CartId, Guid BuyerId, IReadOnlyList<CheckedOutItem> Items)
{
    public const string EventType = "cart.checked-out";
}

public sealed record CheckedOutItem(Guid ProductId, Guid SellerId, string ProductName, decimal UnitPrice, string Currency, int Quantity);

/// <summary>
/// No checkout, registra o que cada vendedor tem a receber pelo pedido (<c>checkoutId</c> = id do pedido): bruto dos
/// itens dele, taxa e líquido. O dinheiro só é liberado na entrega. Idempotente: pedido repetido não duplica.
/// </summary>
public sealed class RecordSaleHandler(IPayoutRepository payouts, PayoutPolicy policy, TimeProvider clock)
    : IIntegrationEventHandler<CartCheckedOut>
{
    public async Task HandleAsync(Guid eventId, CartCheckedOut checkout, CancellationToken cancellationToken)
    {
        if (checkout.Items is not { Count: > 0 })
        {
            throw new InvalidEventException($"Checkout {checkout.CheckoutId} has no items.");
        }

        var existing = (await payouts.ListByOrderAsync(checkout.CheckoutId, cancellationToken))
            .Select(p => p.SellerId)
            .ToHashSet();

        var added = false;
        foreach (var sale in checkout.Items.GroupBy(i => i.SellerId))
        {
            if (existing.Contains(sale.Key))
            {
                continue;
            }
            payouts.Add(Payout.ForSale(checkout.CheckoutId, sale.Key, GrossOf(checkout.CheckoutId, sale), policy.Fee, clock));
            added = true;
        }

        if (!added)
        {
            return;
        }
        try
        {
            await payouts.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicatePayoutException)
        {
            // Mesmo pedido processado em paralelo: a nova tentativa encontra os repasses e não faz nada.
            throw new PayoutConcurrencyException();
        }
    }

    private static Money GrossOf(Guid orderId, IEnumerable<CheckedOutItem> items)
    {
        var list = items.ToList();
        var currencies = list.Select(i => i.Currency?.Trim().ToUpperInvariant()).Distinct().ToList();
        if (currencies.Count != 1)
        {
            throw new InvalidEventException($"Checkout {orderId} mixes currencies for the same seller.");
        }
        if (list.Any(i => i.Quantity <= 0 || i.UnitPrice <= 0))
        {
            throw new InvalidEventException($"Checkout {orderId} has an item without price or quantity.");
        }

        try
        {
            return Money.Create(list.Sum(i => i.UnitPrice * i.Quantity), currencies[0]!);
        }
        catch (DomainException ex)
        {
            throw new InvalidEventException($"Checkout {orderId}: {ex.Message}");
        }
    }
}
