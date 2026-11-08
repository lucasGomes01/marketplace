using BuildingBlocks.Application;
using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.RegisterPayoutCard;

public sealed record RegisterPayoutCardCommand(
    Guid SellerId, string? CardNumber, int? ExpiryMonth, int? ExpiryYear, string? HolderName);

/// <summary>
/// Cadastra (ou troca) o cartão Mastercard em que o vendedor recebe. O número é cifrado antes de gravar.
/// Repasses que esperavam cartão ou foram recusados voltam a ser agendados.
/// </summary>
public sealed class RegisterPayoutCardHandler(
    IPayoutCardRepository cards, IPayoutRepository payouts, ICardProtector protector, PayoutPolicy policy, TimeProvider clock)
    : IHandler<RegisterPayoutCardCommand, PayoutCardResponse>
{
    public async Task<Result<PayoutCardResponse>> HandleAsync(RegisterPayoutCardCommand command, CancellationToken cancellationToken = default)
    {
        CardNumber number;
        CardExpiry expiry;
        var errors = new List<Error>();
        try
        {
            number = CardNumber.Create(command.CardNumber);
        }
        catch (DomainException ex)
        {
            errors.Add(Error.Validation(ex.Field, CodeFor(ex), ex.Message));
            number = null!;
        }
        try
        {
            expiry = CardExpiry.Create(command.ExpiryMonth, command.ExpiryYear, clock);
        }
        catch (DomainException ex)
        {
            errors.Add(Error.Validation(ex.Field, CodeFor(ex), ex.Message));
            expiry = null!;
        }
        if (string.IsNullOrWhiteSpace(command.HolderName))
        {
            errors.Add(Error.Validation("holderName", "holderName.required", "Card holder name is required."));
        }
        if (errors.Count > 0)
        {
            return Result<PayoutCardResponse>.Failure(errors);
        }

        PayoutCard card;
        try
        {
            var protectedNumber = protector.Protect(number.Digits);
            var existing = await cards.FindAsync(command.SellerId, cancellationToken);
            if (existing is null)
            {
                card = PayoutCard.Register(command.SellerId, number, expiry, command.HolderName, protectedNumber, clock);
                cards.Add(card);
            }
            else
            {
                existing.Replace(number, expiry, command.HolderName, protectedNumber, clock);
                card = existing;
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Field, CodeFor(ex), ex.Message);
        }

        foreach (var payout in await payouts.ListWaitingForCardAsync(command.SellerId, cancellationToken))
        {
            payout.CardAvailable(policy.HoldPeriod, clock);
        }
        await payouts.SaveChangesAsync(cancellationToken);

        return PayoutCardResponse.From(card);
    }

    private static string CodeFor(DomainException ex) => (ex.Field, ex.Message) switch
    {
        (Domain.CardNumber.Field, var m) when m.Contains("Mastercard") => "cardNumber.not_mastercard",
        (Domain.CardNumber.Field, var m) when m.Contains("required") => "cardNumber.required",
        (Domain.CardNumber.Field, var m) when m.Contains("16 digits") => "cardNumber.length",
        (Domain.CardNumber.Field, _) => "cardNumber.invalid",
        (CardExpiry.Field, var m) when m.Contains("expired") => "expiry.expired",
        (CardExpiry.Field, _) => "expiry.invalid",
        (var field, _) => $"{field ?? "request"}.invalid",
    };
}
