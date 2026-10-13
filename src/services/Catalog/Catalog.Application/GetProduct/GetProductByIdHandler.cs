using BuildingBlocks.Application;
using Catalog.Application.Abstractions;

namespace Catalog.Application.GetProduct;

public sealed record GetProductByIdQuery(Guid ProductId);

public sealed class GetProductByIdHandler(IProductRepository products) : IHandler<GetProductByIdQuery, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(
        GetProductByIdQuery query, CancellationToken cancellationToken = default)
    {
        var product = await products.FindByIdAsync(query.ProductId, cancellationToken);

        return product is null
            ? Error.NotFound("product.not_found", "Product not found.")
            : ProductResponse.From(product);
    }
}
