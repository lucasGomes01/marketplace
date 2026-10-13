using System.Net;
using System.Net.Http.Json;
using Cart.Application.Abstractions;

namespace Cart.Infrastructure.Catalog;

/// <summary>Adaptador HTTP da porta <see cref="IProductCatalog"/>: chama <c>GET /products/{id}</c> do Catalog.</summary>
public sealed class CatalogHttpClient(HttpClient http) : IProductCatalog
{
    private sealed record ProductDto(Guid Id, Guid SellerId, string Name, decimal Price, string Currency, int Stock);

    public async Task<CatalogProduct?> FindAsync(Guid productId, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"products/{productId}", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Sem conexão ou timeout (não é o chamador cancelando).
            throw new CatalogUnavailableException(ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new CatalogUnavailableException(
                    new HttpRequestException($"Catalog answered {(int)response.StatusCode}.", null, response.StatusCode));
            }

            var dto = await response.Content.ReadFromJsonAsync<ProductDto>(cancellationToken)
                ?? throw new CatalogUnavailableException(new InvalidOperationException("Catalog returned an empty body."));
            return new CatalogProduct(dto.Id, dto.SellerId, dto.Name, dto.Price, dto.Currency, dto.Stock);
        }
    }
}
