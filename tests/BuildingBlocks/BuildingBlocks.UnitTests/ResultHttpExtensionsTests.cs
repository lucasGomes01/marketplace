using BuildingBlocks.Api;
using BuildingBlocks.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.UnitTests;

public class ResultHttpExtensionsTests
{
    private static IResult ToHttp(Result<string> result) => result.ToHttpResult(value => Results.Ok(value));

    [Fact]
    public void Success_uses_the_success_mapping()
    {
        var http = ToHttp("hello");

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(http);
        Assert.Equal("hello", ok.Value);
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(http).StatusCode);
    }

    [Fact]
    public void Validation_errors_become_400_grouped_by_field()
    {
        var http = ToHttp(Result<string>.Failure(
        [
            Error.Validation("password", "password.too_short", "Too short."),
            Error.Validation("password", "password.weak", "Too weak."),
            Error.Validation(null, "request.invalid", "Bad request."),
        ]));

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(http).StatusCode);
        var problem = Assert.IsType<HttpValidationProblemDetails>(Assert.IsAssignableFrom<IValueHttpResult>(http).Value);
        Assert.Equal(["Too short.", "Too weak."], problem.Errors["password"]);
        Assert.Equal(["Bad request."], problem.Errors["request"]);
    }

    [Theory]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unavailable, 503)]
    public void Other_errors_become_problem_details_with_status_title_and_code(ErrorType type, int status)
    {
        var http = ToHttp(new Error("some.code", "Something happened.", type));

        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(http).StatusCode);
        var problem = Assert.IsType<ProblemDetails>(Assert.IsAssignableFrom<IValueHttpResult>(http).Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal("Something happened.", problem.Title);
        Assert.Equal("some.code", problem.Extensions["code"]);
    }
}
