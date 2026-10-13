namespace Cart.Domain;

/// <summary>Item do carrinho com o snapshot do produto (vendedor, nome e preço) do momento em que foi adicionado.</summary>
public sealed class CartItem
{
    public Guid ProductId { get; private set; }
    public Guid SellerId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public Money UnitPrice { get; private set; } = null!;
    public int Quantity { get; private set; }

    public Money Subtotal => UnitPrice.Multiply(Quantity);

    // Usado pelo EF Core.
    private CartItem() { }

    internal CartItem(Guid productId, Guid sellerId, string productName, Money unitPrice, int quantity)
    {
        ProductId = productId;
        SellerId = sellerId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void Refresh(Guid sellerId, string productName, Money unitPrice, int quantity)
    {
        SellerId = sellerId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void SetQuantity(int quantity) => Quantity = quantity;
}
