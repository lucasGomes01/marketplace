using BuildingBlocks.Application;
using Users.Application.Abstractions;

namespace Users.Application.GetUser;

public sealed record GetUserByIdQuery(Guid UserId);

public sealed class GetUserByIdHandler(IUserRepository users) : IHandler<GetUserByIdQuery, UserResponse>
{
    public async Task<Result<UserResponse>> HandleAsync(GetUserByIdQuery query, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(query.UserId, cancellationToken);

        return user is null
            ? Error.NotFound("user.not_found", "User not found.")
            : UserResponse.From(user);
    }
}
