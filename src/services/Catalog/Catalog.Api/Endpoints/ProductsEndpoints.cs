using System.Security.Claims;
using BuildingBlocks.Api;
using BuildingBlocks.Application;
using BuildingBlocks.Auth;
using Catalog.Application;
using Catalog.Application.CreateProduct;
using Catalog.Application.GetProduct;
using Catalog.Application.ListProducts;

namespace Catalog.Api.Endpoints;

/// <summary>Só tradução HTTP ↔ caso de uso: nenhuma regra de negócio aqui.</summary>
public static class ProductsEndpoints
{
    /// <summary>Sem <c>sellerId</c>: o vendedor é sempre quem está autenticado.</summary>
    public sealed record CreateProductRequest(
        string? Name, string? Description, decimal? Price, string? Currency, int? Stock,
        string? Category = null, string? Brand = null, string? Sku = null, string? Condition = null,
        IReadOnlyList<string>? ImageUrls = null, decimal? OriginalPrice = null,
        int? WeightGrams = null, int? LengthCm = null, int? WidthCm = null, int? HeightCm = null,
        int? WarrantyMonths = null, bool? FreeShipping = null, IReadOnlyList<ProductAttributeInput>? Attributes = null);

    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/products");

        products.MapPost("/", CreateAsync)
            .WithName("CreateProduct")
            .RequireAuthorization(MarketplacePolicies.Seller)
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        products.MapGet("/", ListAsync)
            .WithName("ListProducts")
            .Produces<PagedResponse<ProductResponse>>()
            .ProducesValidationProblem();

        products.MapGet("/{id:guid}", GetAsync)
            .WithName("GetProduct")
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateProductRequest request, ClaimsPrincipal principal,
        IHandler<CreateProductCommand, ProductResponse> handler, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } sellerId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(new CreateProductCommand(
            sellerId, request.Name, request.Description, request.Price, request.Currency, request.Stock)
        {
            Category = request.Category,
            Brand = request.Brand,
            Sku = request.Sku,
            Condition = request.Condition,
            ImageUrls = request.ImageUrls,
            OriginalPrice = request.OriginalPrice,
            WeightGrams = request.WeightGrams,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            WarrantyMonths = request.WarrantyMonths,
            FreeShipping = request.FreeShipping,
            Attributes = request.Attributes,
        }, ct);

        return result.ToHttpResult(product => Results.Created($"/products/{product.Id}", product));
    }

    private static async Task<IResult> ListAsync(
        int? page, int? pageSize, string? q, string? category, string? condition,
        decimal? minPrice, decimal? maxPrice, bool? freeShipping, string? sort,
        IHandler<ListProductsQuery, PagedResponse<ProductResponse>> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new ListProductsQuery(page, pageSize)
        {
            Search = q,
            Category = category,
            Condition = condition,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            FreeShipping = freeShipping,
            Sort = sort,
        }, ct);
        return result.ToHttpResult(paged => Results.Ok(paged));
    }

    private static async Task<IResult> GetAsync(
        Guid id, IHandler<GetProductByIdQuery, ProductResponse> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetProductByIdQuery(id), ct);
        return result.ToHttpResult(product => Results.Ok(product));
    }
}
