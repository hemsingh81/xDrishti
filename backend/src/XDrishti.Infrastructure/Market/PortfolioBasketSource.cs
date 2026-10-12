using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

/// <summary>
/// The portfolio buckets as read-only system baskets (ADR 0014). The market and portfolio/accounts modules share one context, so this reads
/// <c>accounts.portfolio_buckets</c>, <c>portfolio.holdings</c> and the master list directly, behind <see cref="IPortfolioBasketSource"/>.
/// </summary>
internal sealed class PortfolioBasketSource(XDrishtiDbContext db) : IPortfolioBasketSource
{
    /// <summary>Shown for a bucket of no account when the owner has not chosen anything: the colour of a new basket.</summary>
    private const string NeutralColour = Basket.DefaultColour;

    private const string AddedBy = "portfolio";

    public async ValueTask<IReadOnlyList<BasketView>> ListAsync(CancellationToken cancellationToken)
    {
        var buckets = await Buckets().ToListAsync(cancellationToken);
        if (buckets.Count == 0)
        {
            return [];
        }

        var ids = buckets.Select(b => b.Id).ToArray();
        var counts = await HeldInstruments()
            .Where(h => ids.Contains(h.BucketId))
            .GroupBy(h => h.BucketId)
            .Select(g => new { BucketId = g.Key, Count = g.Select(h => h.InstrumentId).Distinct().Count() })
            .ToDictionaryAsync(x => x.BucketId, x => x.Count, cancellationToken);

        var ordered = buckets
            .OrderBy(b => b.AccountName is null)
            .ThenBy(b => b.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Id)
            .ToList();
        return
        [
            .. ordered.Select((b, position) => new BasketView(
                b.Id,
                b.Name,
                b.AccountColour ?? NeutralColour,
                BasketPurpose.LongTerm.ToCode(),
                null,
                position,
                counts.GetValueOrDefault(b.Id),
                b.CreatedAt,
                null,
                BasketKinds.Portfolio,
                true,
                b.AccountId,
                b.AccountName)),
        ];
    }

    public async ValueTask<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        await Buckets().AnyAsync(b => b.Id == id, cancellationToken);

    public async ValueTask<BasketMembers?> MembersAsync(Guid id, CancellationToken cancellationToken)
    {
        var bucket = await Buckets().Where(b => b.Id == id).FirstOrDefaultAsync(cancellationToken);
        if (bucket is null)
        {
            return null;
        }

        var rows = await (
            from h in HeldInstruments().Where(h => h.BucketId == id)
            join i in db.Instruments.AsNoTracking() on h.InstrumentId equals i.Id
            orderby i.CanonicalSymbol, h.AccountName, h.HoldingId
            select new
            {
                h.HoldingId,
                h.Qty,
                h.FirstSeenAt,
                h.AccountName,
                InstrumentId = i.Id,
                i.CanonicalSymbol,
                i.Name,
                i.DisplayName,
                i.Type,
                i.Isin,
                i.Series,
                i.Sector,
                i.LotSize,
                i.TickSize,
                i.ExpiryDate,
                i.FnoEligible,
                AsmGsm = i.AsmGsmFlag != null,
                Retired = i.RetiredAt != null,
            }).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.InstrumentId).Distinct().ToArray();
        var tracked = ids.Length == 0
            ? []
            : await db.TrackedInstruments.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);

        // One member per instrument: a custom bucket can hold the same instrument in several accounts, so quantities add up and the account names are listed.
        var items = rows
            .GroupBy(r => r.InstrumentId)
            .Select((g, position) =>
            {
                var first = g.First();
                return new BasketMemberView(
                    first.HoldingId,
                    new InstrumentSummary(
                        first.InstrumentId, first.CanonicalSymbol, first.Name, first.DisplayName, first.Type, first.Isin, first.Series, first.Sector, first.LotSize, first.TickSize,
                        first.ExpiryDate, first.FnoEligible, first.AsmGsm, first.Retired, TrackedInfoMapper.From(tracked.GetValueOrDefault(first.InstrumentId), first.Retired)),
                    position,
                    g.Min(r => r.FirstSeenAt),
                    null,
                    null,
                    AddedBy,
                    g.Sum(r => r.Qty),
                    string.Join(", ", g.Select(r => r.AccountName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
            })
            .ToList();
        return new BasketMembers(id, bucket.Name, null, null, items.Count, items);
    }

    public async ValueTask<PortfolioBasketInstruments?> InstrumentsAsync(Guid id, CancellationToken cancellationToken)
    {
        var bucket = await Buckets().Where(b => b.Id == id).FirstOrDefaultAsync(cancellationToken);
        if (bucket is null)
        {
            return null;
        }

        var instruments = await (
            from h in HeldInstruments().Where(h => h.BucketId == id)
            join i in db.Instruments.AsNoTracking() on h.InstrumentId equals i.Id
            select new { i.Id, i.CanonicalSymbol }).Distinct().ToListAsync(cancellationToken);
        return new PortfolioBasketInstruments(id, bucket.Name, [.. instruments.OrderBy(i => i.CanonicalSymbol, StringComparer.Ordinal).Select(i => i.Id)]);
    }

    /// <summary>
    /// The buckets a user can pick: not archived; custom, or the default bucket of an active account with the Portfolio role. A class with initialisers, not a
    /// record with a constructor: filters on it are translated into the query.
    /// </summary>
    private IQueryable<BucketRow> Buckets() =>
        from b in db.PortfolioBuckets.AsNoTracking()
        where b.ArchivedAt == null
        join a in db.BrokerAccounts.AsNoTracking() on b.DefaultForAccountId equals (Guid?)a.Id into accounts
        from a in accounts.DefaultIfEmpty()
        where b.DefaultForAccountId == null || (a != null && a.ArchivedAt == null && a.Portfolio.Enabled)
        select new BucketRow
        {
            Id = b.Id,
            Name = b.Name,
            CreatedAt = b.CreatedAt,
            AccountId = b.DefaultForAccountId,
            AccountName = a != null ? a.Name : null,
            AccountColour = a != null ? a.Color : null,
        };

    /// <summary>Current holdings (quantity above zero) of active accounts with the Portfolio role that are mapped to an instrument.</summary>
    private IQueryable<HeldRow> HeldInstruments() =>
        from h in db.Holdings.AsNoTracking()
        where h.Qty > 0 && h.InstrumentId != null
        join a in db.BrokerAccounts.AsNoTracking() on h.AccountId equals a.Id
        where a.ArchivedAt == null && a.Portfolio.Enabled
        select new HeldRow
        {
            HoldingId = h.Id,
            BucketId = h.BucketId,
            InstrumentId = h.InstrumentId!.Value,
            Qty = h.Qty,
            FirstSeenAt = h.FirstSeenAt,
            AccountName = a.Name,
        };

    private sealed class BucketRow
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public Guid? AccountId { get; init; }

        public string? AccountName { get; init; }

        public string? AccountColour { get; init; }
    }

    private sealed class HeldRow
    {
        public required Guid HoldingId { get; init; }

        public required Guid BucketId { get; init; }

        public required Guid InstrumentId { get; init; }

        public required long Qty { get; init; }

        public required DateTimeOffset FirstSeenAt { get; init; }

        public required string AccountName { get; init; }
    }
}
