using Catalog.Application.Abstractions;
using Catalog.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

internal sealed class StockStore(CatalogDbContext db) : IStockStore
{
    private static readonly int[] UniqueViolationErrors = [2601, 2627];

    public Task<StockReservation?> FindReservationAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.StockReservations.AsNoTracking().SingleOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<Product>> FindProductsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.ToList();
        return await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(StockReservation reservation, CancellationToken cancellationToken)
    {
        db.StockReservations.Add(reservation);
        try
        {
            // Uma SaveChanges = uma transação: baixa de estoque + reserva + evento no outbox juntos.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outro pedido baixou o estoque dos mesmos produtos (rowversion mudou).
            throw new StockConcurrencyException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number))
        {
            // O mesmo pedido foi reservado em paralelo; na nova tentativa a reserva já existe e nada é feito.
            throw new StockConcurrencyException();
        }
    }
}
