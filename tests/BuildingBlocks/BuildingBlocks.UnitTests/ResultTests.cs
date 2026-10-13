using BuildingBlocks.Application;

namespace BuildingBlocks.UnitTests;

public class ResultTests
{
    [Fact]
    public void Success_exposes_the_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Failure_from_a_single_error()
    {
        Result<int> result = Error.NotFound("thing.not_found", "Thing not found.");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal("thing.not_found", error.Code);
        Assert.Equal(ErrorType.NotFound, error.Type);
    }

    [Fact]
    public void Failure_keeps_all_errors()
    {
        var result = Result<int>.Failure(
        [
            Error.Validation("password", "password.too_short", "Too short."),
            Error.Validation("role", "role.invalid", "Invalid role."),
        ]);

        Assert.Equal(["password", "role"], result.Errors.Select(e => e.Field));
    }

    [Fact]
    public void Reading_value_of_a_failure_throws()
    {
        Result<int> result = Error.Conflict("x", "X.");

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_requires_at_least_one_error()
    {
        Assert.Throws<ArgumentException>(() => Result<int>.Failure([]));
    }
}
