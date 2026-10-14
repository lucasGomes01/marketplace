using Catalog.Domain;

namespace Catalog.Application.Abstractions;

/// <summary>Porta de persistência da reserva de estoque (unidade de trabalho: produtos + reserva).</summary>
public interface IStockStore
{
    Task<StockReservation?> FindReservationAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Produtos existentes entre os ids pedidos, rastreados para alteração.</summary>
    Task<IReadOnlyList<Product>> FindProductsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken);

    /// <summary>Grava a reserva e as alterações de estoque dos produtos carregados, numa transação.</summary>
    /// <exception cref="StockConcurrencyException">Outro pedido alterou os mesmos produtos ao mesmo tempo.</exception>
    Task SaveAsync(StockReservation reservation, CancellationToken cancellationToken);
}

/// <summary>Conflito de concorrência: o evento deve ser reprocessado com dados novos.</summary>
public sealed class StockConcurrencyException() : Exception("Stock was changed concurrently; retry the reservation.");
