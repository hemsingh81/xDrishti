using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>One account with all its settings.</summary>
public sealed record GetAccountQuery(Guid AccountId) : IQuery<Result<AccountDetail>>;

internal sealed class GetAccountHandler(IAccountStore accounts, AccountViews views) : IQueryHandler<GetAccountQuery, Result<AccountDetail>>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(GetAccountQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken);
        return account is null
            ? Result.Failure<AccountDetail>(AccountErrors.NotFound)
            : Result.Success(await views.DetailAsync(account, cancellationToken));
    }
}
