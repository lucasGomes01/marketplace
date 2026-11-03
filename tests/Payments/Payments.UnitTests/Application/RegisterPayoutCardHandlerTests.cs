using BuildingBlocks.Application;
using Microsoft.Extensions.Time.Testing;
using Payments.Application;
using Payments.Application.RegisterPayoutCard;

namespace Payments.UnitTests.Application;

public class RegisterPayoutCardHandlerTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryPaymentsStore _store = new();
    private readonly Guid _seller = Guid.CreateVersion7();

    private Task<Result<PayoutCardResponse>> Register(string? number, int? month = 12, int? year = 2029, string? name = "Maria Silva") =>
        new RegisterPayoutCardHandler(_store, _store, new FakeCardProtector(), PayoutPolicy.Default, _clock)
            .HandleAsync(new RegisterPayoutCardCommand(_seller, number, month, year, name));

    [Fact]
    public async Task Saves_the_card_encrypted_and_answers_only_the_last_digits()
    {
        var result = await Register("5555 5555 5555 4444");

        Assert.True(result.IsSuccess);
        Assert.Equal(new PayoutCardResponse("Mastercard", "4444", 12, 2029, "Maria Silva", _clock.GetUtcNow()), result.Value);
        var card = Assert.Single(_store.Cards);
        Assert.DoesNotContain("5555555555554444", card.ProtectedNumber);
        Assert.Equal("5555555555554444", new FakeCardProtector().Unprotect(card.ProtectedNumber));
    }

    [Fact]
    public async Task Registering_again_replaces_the_card()
    {
        await Register("5555555555554444");

        var result = await Register("5105105105105100");

        Assert.Equal("5100", result.Value.Last4);
        Assert.Single(_store.Cards);
    }

    [Theory]
    [InlineData("4111111111111111", "cardNumber", "cardNumber.not_mastercard")]
    [InlineData("5555555555554445", "cardNumber", "cardNumber.invalid")]
    [InlineData("", "cardNumber", "cardNumber.required")]
    public async Task Invalid_card_number_is_a_validation_error(string number, string field, string code)
    {
        var result = await Register(number);

        var error = Assert.Single(result.Errors);
        Assert.Equal(field, error.Field);
        Assert.Equal(code, error.Code);
        Assert.Empty(_store.Cards);
    }

    [Fact]
    public async Task Reports_every_invalid_field_at_once()
    {
        var result = await Register("4111111111111111", month: 10, year: 2026, name: " ");

        Assert.Equal(["cardNumber", "expiry", "holderName"], result.Errors.Select(e => e.Field!).ToArray());
        Assert.Equal("expiry.expired", result.Errors[1].Code);
    }
}
