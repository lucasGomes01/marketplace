using Cart.Application.Abstractions;
using Cart.Domain;

namespace Cart.UnitTests.Application;

internal sealed class InMemoryCartRepository : ICartRepository
{
    public Dictionary<Guid, ShoppingCart> Carts { get; } = [];
    public int Saves { get; private set; }
    public bool SimulateConcurrentUpdate { get; set; }

    /// <summary>Chamado no começo de cada gravação (ex.: para ver o que já tinha sido registrado no outbox).</summary>
    public Action? BeforeSave { get; set; }

    public Task<ShoppingCart?> FindByBuyerAsync(Guid buyerId, CancellationToken cancellationToken) =>
        Task.FromResult(Carts.GetValueOrDefault(buyerId));

    public Task SaveAsync(ShoppingCart cart, CancellationToken cancellationToken)
    {
        BeforeSave?.Invoke();
        if (SimulateConcurrentUpdate)
        {
            throw new CartConcurrencyException();
        }
        Carts[cart.BuyerId] = cart;
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeProductCatalog : IProductCatalog
{
    public Dictionary<Guid, CatalogProduct> Products { get; } = [];
    public bool Unavailable { get; set; }

    public CatalogProduct Add(string name = "Furadeira", decimal price = 199.90m, int stock = 5, Guid? sellerId = null)
    {
        var product = new CatalogProduct(Guid.CreateVersion7(), sellerId ?? Guid.CreateVersion7(), name, price, "BRL", stock);
        Products[product.Id] = product;
        return product;
    }

    public Task<CatalogProduct?> FindAsync(Guid productId, CancellationToken cancellationToken) =>
        Unavailable
            ? throw new CatalogUnavailableException(new HttpRequestException("connection refused"))
            : Task.FromResult(Products.GetValueOrDefault(productId));
}
