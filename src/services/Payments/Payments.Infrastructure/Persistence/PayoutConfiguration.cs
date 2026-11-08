using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

internal sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("Payouts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        // Um repasse por pedido + vendedor: o mesmo checkout processado duas vezes não duplica.
        builder.HasIndex(p => new { p.OrderId, p.SellerId }).IsUnique();
        builder.HasIndex(p => new { p.SellerId, p.CreatedAt });
        builder.HasIndex(p => new { p.Status, p.ScheduledFor });

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(p => p.GrossAmount).HasPrecision(18, 2);
        builder.Property(p => p.FeeAmount).HasPrecision(18, 2);
        builder.Property(p => p.NetAmount).HasPrecision(18, 2);
        builder.Property(p => p.ProviderReference).HasMaxLength(100);
        builder.Property(p => p.LastError).HasMaxLength(Payout.MaxReasonLength);
        // Concorrência otimista: dois despachantes não marcam o mesmo repasse.
        builder.Property<byte[]>("Version").IsRowVersion();

        builder.Ignore(p => p.Gross);
        builder.Ignore(p => p.Fee);
        builder.Ignore(p => p.Net);
        builder.Ignore(p => p.TransferReference);
    }
}
