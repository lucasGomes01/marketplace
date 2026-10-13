using BuildingBlocks.Application;
using Microsoft.Extensions.Time.Testing;
using Users.Application.BecomeSeller;
using Users.Domain;

namespace Users.UnitTests.Application;

public class BecomeSellerHandlerTests
{
    private readonly InMemoryUserRepository _repository = new();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly FakeTokenIssuer _tokens;
    private readonly BecomeSellerHandler _handler;
    private readonly User _maria;

    public BecomeSellerHandlerTests()
    {
        _tokens = new FakeTokenIssuer(_clock);
        _handler = new BecomeSellerHandler(_repository, _tokens, _clock);
        _maria = User.Register("maria@example.com", "Maria", "hash", _clock);
        _repository.Users.Add(_maria);
    }

    [Fact]
    public async Task Activates_the_seller_profile_and_saves_it()
    {
        var result = await _handler.HandleAsync(new BecomeSellerCommand(_maria.Id));

        Assert.True(_maria.IsSeller);
        Assert.Equal(1, _repository.Updates);
        Assert.Equal([UserRole.Buyer, UserRole.Seller], result.Value.User.Roles);
    }

    [Fact]
    public async Task Returns_a_new_token_with_the_seller_role()
    {
        var result = await _handler.HandleAsync(new BecomeSellerCommand(_maria.Id));

        Assert.Equal($"token-for-{_maria.Id}", result.Value.AccessToken);
        Assert.Equal(_clock.GetUtcNow().AddHours(1), result.Value.ExpiresAt);
        Assert.Equal([UserRole.Buyer, UserRole.Seller], _tokens.LastIssued!.Value.Roles);
    }

    [Fact]
    public async Task Is_idempotent_for_who_already_sells()
    {
        _maria.BecomeSeller(_clock);
        var since = _maria.SellerSince;
        _clock.Advance(TimeSpan.FromDays(1));

        var result = await _handler.HandleAsync(new BecomeSellerCommand(_maria.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(since, _maria.SellerSince);
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        var result = await _handler.HandleAsync(new BecomeSellerCommand(Guid.CreateVersion7()));

        Assert.Equal("user.not_found", Assert.Single(result.Errors).Code);
        Assert.Equal(0, _repository.Updates);
    }
}
