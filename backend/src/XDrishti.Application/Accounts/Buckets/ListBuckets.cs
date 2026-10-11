using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Accounts.Buckets;

/// <summary>All portfolio buckets.</summary>
public sealed record ListBucketsQuery : IQuery<IReadOnlyList<BucketView>>;

internal sealed class ListBucketsHandler(IBucketStore buckets) : IQueryHandler<ListBucketsQuery, IReadOnlyList<BucketView>>
{
    public async ValueTask<IReadOnlyList<BucketView>> HandleAsync(ListBucketsQuery query, CancellationToken cancellationToken) =>
        [.. (await buckets.ListAsync(cancellationToken)).Select(b => new BucketView(b.Id, b.Name))];
}
