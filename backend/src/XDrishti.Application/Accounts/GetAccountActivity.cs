using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>The latest audit entries of one account (archived accounts included), newest first.</summary>
public sealed record GetAccountActivityQuery(Guid AccountId, int Take = GetAccountActivityQuery.DefaultTake) : IQuery<Result<IReadOnlyList<AccountEventView>>>
{
    public const int DefaultTake = 50;

    public const int MaxTake = 200;
}

internal sealed class GetAccountActivityHandler(IAccountStore accounts, IAccountEventLog events)
    : IQueryHandler<GetAccountActivityQuery, Result<IReadOnlyList<AccountEventView>>>
{
    public async ValueTask<Result<IReadOnlyList<AccountEventView>>> HandleAsync(GetAccountActivityQuery query, CancellationToken cancellationToken)
    {
        if (!await accounts.ExistsAsync(query.AccountId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<AccountEventView>>(AccountErrors.NotFound);
        }

        var take = Math.Clamp(query.Take, 1, GetAccountActivityQuery.MaxTake);
        var list = await events.ListForAccountAsync(query.AccountId, take, cancellationToken);
        IReadOnlyList<AccountEventView> views = [.. list.Select(e => new AccountEventView(e.At, e.Event.ToString(), e.Detail, e.Actor))];
        return Result.Success(views);
    }
}
