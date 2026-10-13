using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

internal sealed class HoldingQueries(XDrishtiDbContext db) : IHoldingQueries
{
    public async ValueTask<IReadOnlyList<HoldingData>> ListAsync(HoldingFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Joined();
        if (filter.AccountId is { } accountId)
        {
            query = query.Where(x => x.Holding.AccountId == accountId);
        }
        else
        {
            query = query.Where(x => x.PortfolioOn && (!filter.TotalsOnly || x.InTotals));
        }

        if (filter.BucketId is { } bucketId)
        {
            query = query.Where(x => x.Holding.BucketId == bucketId);
        }

        if (filter.Status is { } status)
        {
            var wanted = status == "closed" ? HoldingStatus.Closed : HoldingStatus.Open;
            query = query.Where(x => x.Holding.Status == wanted);
        }

        if (filter.Text is { } text)
        {
            var pattern = "%" + LikePattern.Escape(text) + "%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Holding.TradingSymbol, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(x.Holding.Name, pattern, LikePattern.EscapeCharacter)
                || (x.Holding.Isin != null && EF.Functions.ILike(x.Holding.Isin, pattern, LikePattern.EscapeCharacter)));
        }

        return await ReadAsync(query, cancellationToken);
    }

    public async ValueTask<HoldingData?> GetAsync(Guid holdingId, CancellationToken cancellationToken)
    {
        var found = await ReadAsync(Joined().Where(x => x.Holding.Id == holdingId), cancellationToken);
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>Holdings of active accounts with the names of account, bucket and (when mapped) instrument.</summary>
    private IQueryable<JoinedRow> Joined() =>
        from holding in db.Holdings.AsNoTracking()
        join account in db.BrokerAccounts.AsNoTracking() on holding.AccountId equals account.Id
        join bucket in db.PortfolioBuckets.AsNoTracking() on holding.BucketId equals bucket.Id
        join instrument in db.Instruments.AsNoTracking() on holding.InstrumentId equals instrument.Id into instruments
        from instrument in instruments.DefaultIfEmpty()
        where account.ArchivedAt == null
        select new JoinedRow
        {
            Holding = holding,
            AccountName = account.Name,
            PortfolioOn = account.Portfolio.Enabled,
            InTotals = account.Portfolio.IncludeInTotals,
            BucketName = bucket.Name,
            CanonicalSymbol = instrument.CanonicalSymbol,
        };

    private async ValueTask<IReadOnlyList<HoldingData>> ReadAsync(IQueryable<JoinedRow> query, CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Holding.Id).ToArray();
        var lots = (await db.HoldingLots.AsNoTracking().Where(l => ids.Contains(l.HoldingId)).ToListAsync(cancellationToken))
            .ToLookup(l => l.HoldingId);
        return
        [
            .. rows.Select(r => new HoldingData(
                r.Holding.Id,
                r.Holding.AccountId,
                r.AccountName,
                r.Holding.BucketId,
                r.BucketName,
                r.Holding.InstrumentId,
                r.CanonicalSymbol,
                r.Holding.Exchange,
                r.Holding.SecurityId,
                r.Holding.Isin,
                r.Holding.TradingSymbol,
                r.Holding.Name,
                r.Holding.Qty,
                r.Holding.AvgCost,
                r.Holding.LastPrice,
                r.Holding.LastPriceAt,
                r.Holding.Status.ToString().ToLowerInvariant(),
                r.Holding.FirstSeenAt,
                r.Holding.ClosedAt,
                r.Holding.SyncedAt,
                [.. lots[r.Holding.Id].Select(l => new LotData(
                    l.Id, l.AcquiredOn, l.Qty, l.UnitCost, l.Source.ToString().ToLowerInvariant(), l.UsesDefaultDate, l.ClosedOn, l.ClosePrice, l.Estimated))],
                r.Holding.PreviousClose,
                r.Holding.LastPriceSource.ToString().ToLowerInvariant())),
        ];
    }

    /// <summary>A class with initialisers, not a record with a constructor: filters on it are translated into the query.</summary>
    private sealed class JoinedRow
    {
        public required Holding Holding { get; init; }

        public required string AccountName { get; init; }

        public required bool PortfolioOn { get; init; }

        public required bool InTotals { get; init; }

        public required string BucketName { get; init; }

        public string? CanonicalSymbol { get; init; }
    }
}
