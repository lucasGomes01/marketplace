using BuildingBlocks.Application;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Api;

public static class ResultHttpExtensions
{
    /// <summary>
    /// Traduz o resultado de um caso de uso para HTTP. Erros de validação viram um único 400 (agrupados por campo);
    /// os demais viram ProblemDetails com o status do tipo e o <c>code</c> nas extensões.
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        var validation = result.Errors.Where(e => e.Type == ErrorType.Validation).ToList();
        if (validation.Count > 0)
        {
            return Results.ValidationProblem(validation
                .GroupBy(e => e.Field ?? "request")
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));
        }

        var error = result.Errors[0];
        return Results.Problem(
            statusCode: StatusCodeOf(error.Type),
            title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    private static int StatusCodeOf(ErrorType type) => type switch
    {
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };
}
