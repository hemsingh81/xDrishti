using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The instruments a Portfolio screen follows (ADR 0024 §7): the open holdings with an instrument in the scope the summary is made of - without an account those of the accounts that are active, have the Portfolio role and count in totals
/// (<c>HoldingFilter</c> with <c>TotalsOnly</c>), with one those of that account whatever its role - narrowed to a bucket, plus the listed holdings of any account that is not archived. Largest value (quantity times last price, over the
/// holdings of the instrument) first, each instrument once. Reads three columns of the rows involved; no tracking.
/// </summary>
internal sealed class LiveScopeResolver(XDrishtiDbContext db) : ILiveScopeResolver
{
    public async ValueTask<IReadOnlyList<Guid>> ResolveAsync(Guid? accountId, Guid? bucketId, IReadOnlyCollection<Guid> holdingIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(holdingIds);
        var open =
            from holding in db.Holdings.AsNoTracking()
            join account in db.BrokerAccounts.AsNoTracking() on holding.AccountId equals account.Id
            where account.ArchivedAt == null && holding.Status == HoldingStatus.Open && holding.InstrumentId != null
            select new { Holding = holding, account.Portfolio.Enabled, account.Portfolio.IncludeInTotals };

        var scoped = accountId is { } one
            ? open.Where(x => x.Holding.AccountId == one)
            : open.Where(x => x.Enabled && x.IncludeInTotals);
        if (bucketId is { } bucket)
        {
            scoped = scoped.Where(x => x.Holding.BucketId == bucket);
        }

        var rows = await scoped.Select(x => new Row(x.Holding.Id, x.Holding.InstrumentId!.Value, x.Holding.Qty, x.Holding.LastPrice)).ToListAsync(cancellationToken);
        if (holdingIds.Count > 0)
        {
            var wanted = holdingIds.ToArray();
            var listed = await open.Where(x => wanted.Contains(x.Holding.Id))
                .Select(x => new Row(x.Holding.Id, x.Holding.InstrumentId!.Value, x.Holding.Qty, x.Holding.LastPrice))
                .ToListAsync(cancellationToken);
            var known = rows.Select(r => r.Id).ToHashSet();
            rows.AddRange(listed.Where(r => !known.Contains(r.Id)));
        }

        return
        [
            .. rows.GroupBy(r => r.InstrumentId)
                .Select(g => (InstrumentId: g.Key, Value: g.Sum(r => r.Qty * r.LastPrice)))
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.InstrumentId)
                .Select(x => x.InstrumentId),
        ];
    }

    private sealed record Row(Guid Id, Guid InstrumentId, long Qty, decimal LastPrice);
}
