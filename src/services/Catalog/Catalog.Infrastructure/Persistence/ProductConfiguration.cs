using System.Text.Json;
using Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);

        // Money é um value object: vira duas colunas na própria tabela de produtos.
        builder.ComplexProperty(p => p.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("Price").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsFixedLength();
        });

        builder.Property(p => p.Category).HasMaxLength(Product.CategoryMaxLength).IsRequired()
            .HasDefaultValue(ProductCategories.Default);
        builder.Property(p => p.Brand).HasMaxLength(Product.BrandMaxLength);
        builder.Property(p => p.Sku).HasMaxLength(Product.SkuMaxLength);
        builder.Property(p => p.Condition).HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(ProductCondition.New);
        builder.Property(p => p.OriginalPrice).HasPrecision(18, 2);

        // Listas pequenas e sempre lidas junto com o produto: JSON numa coluna, sem tabela à parte.
        builder.PrimitiveCollection(p => p.ImageUrls).IsRequired();
        builder.Property(p => p.Attributes)
            .HasConversion(
                list => JsonSerializer.Serialize(list, JsonOptions),
                json => JsonSerializer.Deserialize<List<ProductAttribute>>(json, JsonOptions) ?? new List<ProductAttribute>(),
                new ValueComparer<List<ProductAttribute>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                    list => list.ToList()))
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        // Concorrência otimista: duas reservas da mesma última unidade não podem ambas baixar o estoque.
        builder.Property<byte[]>("Version").IsRowVersion();

        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex(p => p.SellerId);
        builder.HasIndex(p => p.Category);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
