using Cart.Domain;

namespace Cart.Application.Abstractions;

/// <summary>Porta de persistência do carrinho (EF Core na Infrastructure).</summary>
public interface ICartRepository
{
    Task<ShoppingCart?> FindByBuyerAsync(Guid buyerId, CancellationToken cancellationToken);

    /// <summary>Insere ou atualiza.</summary>
    /// <exception cref="CartConcurrencyException">O carrinho foi alterado por outra requisição no meio do caminho.</exception>
    Task SaveAsync(ShoppingCart cart, CancellationToken cancellationToken);
}

public sealed class CartConcurrencyException() : Exception("The cart was changed by another request.");

/// <summary>Dados do produto que o carrinho precisa, vindos do Catalog na hora.</summary>
public sealed record CatalogProduct(Guid Id, Guid SellerId, string Name, decimal Price, string Currency, int Stock);

/// <summary>Porta de consulta ao Catalog (HTTP na Infrastructure).</summary>
public interface IProductCatalog
{
    /// <returns>O produto, ou <c>null</c> se não existe.</returns>
    /// <exception cref="CatalogUnavailableException">O Catalog não respondeu ou respondeu com erro.</exception>
    Task<CatalogProduct?> FindAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed class CatalogUnavailableException(Exception inner) : Exception("The catalog is unavailable.", inner);
