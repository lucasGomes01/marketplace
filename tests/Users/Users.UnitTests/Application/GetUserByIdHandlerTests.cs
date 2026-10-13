using BuildingBlocks.Application;
using Microsoft.Extensions.Time.Testing;
using Users.Application.GetUser;
using Users.Domain;

namespace Users.UnitTests.Application;

public class GetUserByIdHandlerTests
{
    private readonly InMemoryUserRepository _repository = new();

    [Fact]
    public async Task Returns_the_user()
    {
        var user = User.Register("maria@example.com", "Maria", "hash", new FakeTimeProvider());
        _repository.Users.Add(user);

        var result = await new GetUserByIdHandler(_repository).HandleAsync(new GetUserByIdQuery(user.Id));

        Assert.Equal(user.Id, result.Value.Id);
        Assert.Equal("maria@example.com", result.Value.Email);
        Assert.Equal([UserRole.Buyer], result.Value.Roles);
    }

    [Fact]
    public async Task Missing_user_is_not_found()
    {
        var result = await new GetUserByIdHandler(_repository).HandleAsync(new GetUserByIdQuery(Guid.CreateVersion7()));

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("user.not_found", error.Code);
    }
}
