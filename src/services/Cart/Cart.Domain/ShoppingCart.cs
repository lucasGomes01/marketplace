namespace Cart.Domain;

/// <summary>
/// Carrinho de um comprador. Pode ter itens de vários vendedores, mas uma moeda só.
/// Estoque e "não comprar o próprio produto" são verificados na Application, que conhece o Catalog.
/// </summary>
public sealed class ShoppingCart
{
    public const int MaxQuantityPerItem = 99;

    private readonly List<CartItem> _items = [];

    public Guid Id { get; private set; }
    public Guid BuyerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Itens na ordem em que foram adicionados.</summary>
    public IReadOnlyList<CartItem> Items => _items.OrderBy(i => i.Position).ToList();

    public string Currency => _items.Count > 0 ? _items[0].UnitPrice.Currency : Money.DefaultCurrency;


    public Money Total => _items.Aggregate(Money.Zero(Currency), (total, item) => total.Add(item.Subtotal));

    public int TotalQuantity => _items.Sum(i => i.Quantity);

    // Usado pelo EF Core.
    private ShoppingCart() { }

    public static ShoppingCart Create(Guid buyerId, TimeProvider clock)
    {
        if (buyerId == Guid.Empty)
        {
            throw new DomainException("Buyer is required.", "buyerId");
        }

        var now = clock.GetUtcNow();
        return new ShoppingCart { Id = Guid.CreateVersion7(), BuyerId = buyerId, CreatedAt = now, UpdatedAt = now };
    }

    public int QuantityOf(Guid productId) => Find(productId)?.Quantity ?? 0;

    /// <summary>Adiciona o produto ou, se já estiver no carrinho, soma a quantidade e atualiza o snapshot.</summary>
    public void AddItem(Guid productId, Guid sellerId, string productName, Money unitPrice, int quantity, TimeProvider clock)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainException("Product is required.", "productId");
        }
        if (sellerId == Guid.Empty)
        {
            throw new DomainException("Seller is required.", "sellerId");
        }
        var name = (productName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new DomainException("Product name is required.", "productName");
        }
        if (_items.Count > 0 && unitPrice.Currency != Currency)
        {
            throw new DomainException($"This cart uses {Currency}; cannot add an item in {unitPrice.Currency}.", "currency");
        }

        var existing = Find(productId);
        var newQuantity = EnsureValidQuantity((existing?.Quantity ?? 0) + EnsureValidQuantity(quantity));

        if (existing is null)
        {
            var position = _items.Count == 0 ? 1 : _items.Max(i => i.Position) + 1;
            _items.Add(new CartItem(productId, sellerId, name, unitPrice, newQuantity, position));
        }
        else
        {
            existing.Refresh(sellerId, name, unitPrice, newQuantity);
        }
        Touch(clock);
    }

    public void ChangeQuantity(Guid productId, int quantity, TimeProvider clock)
    {
        var item = FindOrThrow(productId);
        item.SetQuantity(EnsureValidQuantity(quantity));
        Touch(clock);
    }

    public void RemoveItem(Guid productId, TimeProvider clock)
    {
        _items.Remove(FindOrThrow(productId));
        Touch(clock);
    }

    public void Clear(TimeProvider clock)
    {
        _items.Clear();
        Touch(clock);
    }

    private CartItem? Find(Guid productId) => _items.SingleOrDefault(i => i.ProductId == productId);

    private CartItem FindOrThrow(Guid productId) =>
        Find(productId) ?? throw new DomainException("This product is not in the cart.", "productId");

    private static int EnsureValidQuantity(int quantity) =>
        quantity is >= 1 and <= MaxQuantityPerItem
            ? quantity
            : throw new DomainException($"Quantity must be between 1 and {MaxQuantityPerItem}.", "quantity");

    private void Touch(TimeProvider clock) => UpdatedAt = clock.GetUtcNow();
}
