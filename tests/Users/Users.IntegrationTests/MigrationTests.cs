using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Users.Infrastructure.Persistence;

namespace Users.IntegrationTests;

/// <summary>Migrations que mexem em dados existentes são testadas contra um banco no formato antigo.</summary>
[Collection(UsersApiCollection.Name)]
public class MigrationTests(UsersApiFactory factory)
{
    private const string InitialCreate = "20260929160639_InitialCreate";

    [Fact]
    public async Task Existing_sellers_become_buyers_and_sellers_and_buyers_stay_buyers()
    {
        var connectionString = new SqlConnectionStringBuilder(factory.SqlServerConnectionString)
        {
            InitialCatalog = $"UsersMigration_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new UsersDbContext(
            new DbContextOptionsBuilder<UsersDbContext>().UseSqlServer(connectionString).Options);
        var migrator = db.GetService<IMigrator>();

        // Banco no formato antigo (coluna Role) com um vendedor e um comprador.
        await migrator.MigrateAsync(InitialCreate);
        var sellerId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var createdAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO [Users] ([Id], [Email], [Name], [Role], [PasswordHash], [CreatedAt]) VALUES
            ({sellerId}, 'seller@example.com', 'Seller', 'Seller', 'hash', {createdAt}),
            ({buyerId}, 'buyer@example.com', 'Buyer', 'Buyer', 'hash', {createdAt})
            """);

        await migrator.MigrateAsync();

        var seller = await db.Users.AsNoTracking().SingleAsync(u => u.Id == sellerId);
        var buyer = await db.Users.AsNoTracking().SingleAsync(u => u.Id == buyerId);
        Assert.True(seller.IsSeller);
        Assert.Equal(createdAt, seller.SellerSince);
        Assert.False(buyer.IsSeller);
        Assert.Null(buyer.SellerSince);

        await db.Database.EnsureDeletedAsync();
    }
}
