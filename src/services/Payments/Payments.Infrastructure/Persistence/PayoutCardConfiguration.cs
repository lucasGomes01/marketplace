using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

internal sealed class PayoutCardConfiguration : IEntityTypeConfiguration<PayoutCard>
{
    public void Configure(EntityTypeBuilder<PayoutCard> builder)
    {
        builder.ToTable("PayoutCards");
        builder.HasKey(c => c.SellerId);
        builder.Property(c => c.SellerId).ValueGeneratedNever();
        builder.Property(c => c.HolderName).HasMaxLength(PayoutCard.HolderNameMaxLength);
        builder.Property(c => c.Last4).HasMaxLength(4).IsFixedLength();
        // Número cifrado (AES-GCM, base64): nunca o número em texto.
        builder.Property(c => c.ProtectedNumber).HasMaxLength(200);
        builder.Ignore(c => c.Expiry);
    }
}
