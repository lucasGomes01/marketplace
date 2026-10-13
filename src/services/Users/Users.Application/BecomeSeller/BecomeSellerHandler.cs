using BuildingBlocks.Application;
using Users.Application.Abstractions;

namespace Users.Application.BecomeSeller;

public sealed record BecomeSellerCommand(Guid UserId);

/// <summary>
/// O token antigo não tem o papel Seller; por isso a resposta já traz um token novo, e o front troca a sessão
/// sem a pessoa precisar sair e entrar de novo.
/// </summary>
public sealed record BecomeSellerResponse(UserResponse User, string AccessToken, DateTimeOffset ExpiresAt);

public sealed class BecomeSellerHandler(IUserRepository users, ITokenIssuer tokens, TimeProvider clock)
    : IHandler<BecomeSellerCommand, BecomeSellerResponse>
{
    public async Task<Result<BecomeSellerResponse>> HandleAsync(
        BecomeSellerCommand command, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("user.not_found", "User not found.");
        }

        if (!user.IsSeller)
        {
            user.BecomeSeller(clock);
            await users.UpdateAsync(user, cancellationToken);
        }

        var token = tokens.Issue(user.Id, user.Email.Value, user.Roles);
        return new BecomeSellerResponse(UserResponse.From(user), token.Value, token.ExpiresAt);
    }
}
