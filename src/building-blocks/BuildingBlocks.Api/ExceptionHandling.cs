using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Api;

public static class ExceptionHandling
{
    /// <summary>
    /// Tratamento de exceções padrão dos serviços: responde ProblemDetails e preserva o status de erros do cliente.
    /// Sem isso, em Development o ASP.NET lança <see cref="BadHttpRequestException"/> para JSON malformado e o
    /// tratador transformava em 500 — quando o certo é 400.
    /// </summary>
    public static IApplicationBuilder UseMarketplaceExceptionHandler(this IApplicationBuilder app) =>
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode
                : StatusCodes.Status500InternalServerError,
        });
}
