using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Accounts;

/// <summary>Every active account with its roles, in the order they were created.</summary>
public sealed record ListAccountsQuery : IQuery<IReadOnlyList<AccountSummary>>;

internal sealed class ListAccountsHandler(IAccountStore accounts, AccountViews views) : IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountSummary>>
{
    public async ValueTask<IReadOnlyList<AccountSummary>> HandleAsync(ListAccountsQuery query, CancellationToken cancellationToken) =>
        await views.SummariesAsync(await accounts.ListActiveAsync(cancellationToken), cancellationToken);
}
