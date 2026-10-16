using XDrishti.Application.Abstractions.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>The signed-in owner's own details.</summary>
public sealed record GetOwnerAccountQuery(Guid SessionId) : IQuery<Result<AccountResponse>>;

/// <summary>What the owner may see about their own account (never the hash or the lockout state).</summary>
public sealed record AccountResponse(string Email, string DisplayName, DateTimeOffset CreatedAt, DateTimeOffset PasswordChangedAt)
{
    internal static AccountResponse From(OwnerAccount owner) => new(owner.Email, owner.DisplayName, owner.CreatedAt, owner.PasswordChangedAt);
}

internal sealed class GetOwnerAccountHandler(ISessionStore sessions, TimeProvider clock) : IQueryHandler<GetOwnerAccountQuery, Result<AccountResponse>>
{
    public async ValueTask<Result<AccountResponse>> HandleAsync(GetOwnerAccountQuery query, CancellationToken cancellationToken)
    {
        var found = await sessions.FindByIdAsync(query.SessionId, cancellationToken);
        if (found is null || found.Session.Evaluate(clock.GetUtcNow()) != SessionStatus.Active)
        {
            return Result.Failure<AccountResponse>(AuthErrors.Unauthenticated);
        }

        return Result.Success(AccountResponse.From(found.Owner));
    }
}
