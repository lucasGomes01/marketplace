using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

public static class OutboxModelBuilderExtensions
{
    public const string TableName = "OutboxMessages";

    /// <summary>
    /// Mapeia a tabela <c>OutboxMessages</c> no DbContext do serviço (chame no <c>OnModelCreating</c> e gere a
    /// migration). Fica no banco do próprio serviço: é isso que permite gravar dado e evento na mesma transação.
    /// </summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable(TableName);
            outbox.HasKey(m => m.Sequence);
            outbox.Property(m => m.Sequence).UseIdentityColumn();
            outbox.HasIndex(m => m.EventId).IsUnique();
            outbox.Property(m => m.Type).HasMaxLength(200).IsRequired();
            outbox.Property(m => m.Key).HasMaxLength(200).IsRequired();
            outbox.Property(m => m.Payload).IsRequired();
            outbox.Property(m => m.TraceParent).HasMaxLength(100);
            outbox.Property(m => m.LastError).HasMaxLength(2000);
            // O relay só procura pendentes, em ordem: índice filtrado pequeno, que não cresce com o histórico.
            outbox.HasIndex(m => m.Sequence).HasFilter("[PublishedAt] IS NULL").HasDatabaseName("IX_OutboxMessages_Pending");
            outbox.HasIndex(m => m.PublishedAt).HasFilter("[PublishedAt] IS NOT NULL");
        });
        return modelBuilder;
    }
}
