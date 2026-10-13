using Cart.Application.Abstractions;
using Cart.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Cart.Infrastructure.Persistence;

/// <summary>Repositório por requisição (scoped): lembra quais itens cada carrinho tinha ao ser carregado.</summary>
internal sealed class CartRepository(CartDbContext db) : ICartRepository
{
    private static readonly int[] UniqueViolationErrors = [2601, 2627];

    private readonly Dictionary<Guid, HashSet<Guid>> _loadedItems = [];

    public async Task<ShoppingCart?> FindByBuyerAsync(Guid buyerId, CancellationToken cancellationToken)
    {
        var cart = await db.Carts.SingleOrDefaultAsync(c => c.BuyerId == buyerId, cancellationToken);
        if (cart is not null)
        {
            _loadedItems[cart.Id] = cart.Items.Select(i => i.ProductId).ToHashSet();
        }
        return cart;
    }

    public async Task SaveAsync(ShoppingCart cart, CancellationToken cancellationToken)
    {
        if (db.Entry(cart).State == EntityState.Detached)
        {
            db.Carts.Add(cart);
        }
        else
        {
            MarkNewItemsAsAdded(cart);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            _loadedItems[cart.Id] = cart.Items.Select(i => i.ProductId).ToHashSet();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra requisição alterou o carrinho depois que ele foi lido.
            throw new CartConcurrencyException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number))
        {
            // Duas requisições criaram o primeiro carrinho do mesmo comprador ao mesmo tempo.
            throw new CartConcurrencyException();
        }
    }

    /// <summary>
    /// Num item de coleção owned com chave preenchida pela aplicação (CartId + ProductId), o EF não sabe se o item é
    /// novo e o marca como Modified — gerando um UPDATE de 0 linhas, que vira falso conflito de concorrência.
    /// Quem sabe a verdade é o repositório: item que não existia na carga é novo.
    /// </summary>
    private void MarkNewItemsAsAdded(ShoppingCart cart)
    {
        db.ChangeTracker.DetectChanges();
        var loaded = _loadedItems.GetValueOrDefault(cart.Id) ?? [];

        foreach (var entry in db.ChangeTracker.Entries<CartItem>())
        {
            if (entry.State == EntityState.Modified && !loaded.Contains(entry.Entity.ProductId))
            {
                entry.State = EntityState.Added;
            }
        }
    }
}
