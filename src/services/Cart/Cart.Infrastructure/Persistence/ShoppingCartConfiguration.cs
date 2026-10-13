using Cart.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cart.Infrastructure.Persistence;

internal sealed class ShoppingCartConfiguration : IEntityTypeConfiguration<ShoppingCart>
{
    public const string VersionColumn = "Version";

    public void Configure(EntityTypeBuilder<ShoppingCart> builder)
    {
        builder.ToTable("Carts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasIndex(c => c.BuyerId).IsUnique();

        // Concorrência otimista. Toda alteração no domínio muda UpdatedAt, então a linha do carrinho sempre é
        // atualizada (mesmo quando só os itens mudam) e o rowversion é conferido.
        builder.Property<byte[]>(VersionColumn).IsRowVersion();

        // Valores calculados no domínio: não viram colunas.
        builder.Ignore(c => c.Total);
        builder.Ignore(c => c.TotalQuantity);
        builder.Ignore(c => c.Currency);

        builder.OwnsMany(c => c.Items, items =>
        {
            items.ToTable("CartItems");
            items.WithOwner().HasForeignKey("CartId");
            items.HasKey("CartId", nameof(CartItem.ProductId));
            items.Property(i => i.ProductName).HasMaxLength(120).IsRequired();
            items.Ignore(i => i.Subtotal);
            items.OwnsOne(i => i.UnitPrice, price =>
            {
                price.Property(m => m.Amount).HasColumnName("UnitPrice").HasPrecision(18, 2);
                price.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsFixedLength();
            });
            items.Navigation(i => i.UnitPrice).IsRequired();
        });
        // Items é calculado (ordenado por Position); o EF usa o campo _items.
        builder.Navigation(c => c.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
