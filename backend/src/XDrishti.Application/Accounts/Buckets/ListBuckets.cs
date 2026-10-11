using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Accounts.Buckets;

/// <summary>
/// The buckets that can be chosen: custom buckets and the default buckets of active accounts that have the Portfolio role. The default bucket of an
/// account whose Portfolio role is off is kept (with its holdings) but not listed.
/// </summary>
public sealed record ListBucketsQuery : IQuery<IReadOnlyList<BucketView>>;

internal sealed class ListBucketsHandler(IBucketStore buckets, IAccountStore accounts) : IQueryHandler<ListBucketsQuery, IReadOnlyList<BucketView>>
{
    public async ValueTask<IReadOnlyList<BucketView>> HandleAsync(ListBucketsQuery query, CancellationToken cancellationToken)
    {
        var all = await buckets.ListAsync(cancellationToken);
        var portfolioAccounts = await accounts.ListPortfolioAccountNamesAsync(cancellationToken);

        var result = new List<BucketView>(all.Count);
        foreach (var bucket in all)
        {
            if (bucket.DefaultForAccountId is not { } accountId)
            {
                result.Add(new BucketView(bucket.Id, bucket.Name, false, null, null));
            }
            else if (portfolioAccounts.TryGetValue(accountId, out var accountName))
            {
                result.Add(new BucketView(bucket.Id, bucket.Name, true, accountId, accountName));
            }
        }

        return result;
    }
}
