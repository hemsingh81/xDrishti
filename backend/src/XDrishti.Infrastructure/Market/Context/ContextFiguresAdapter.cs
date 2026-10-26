using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Market.Context;
using XDrishti.Indicators;

namespace XDrishti.Infrastructure.Market.Context;

/// <summary>
/// <see cref="IContextFigures"/> over the stored days (P3-01d, ADR 0033, ADR 0039): the one project that names <see cref="MarketContext"/>. The built context is cached by the stored days' count and newest date and rebuilt when either changes
/// (1 300 days build in a few milliseconds). The age bound of the library (<see cref="ContextOptions.MaxAgeDays"/>) decides what is stale.
/// </summary>
internal sealed class ContextFiguresAdapter(IContextDayStore store, ContextFiguresAdapter.Cache cache) : IContextFigures
{
    /// <summary>The built context and what it was built from: replaced whenever the stored days change (their count or newest date), shared by every reader (a singleton).</summary>
    internal sealed class Cache
    {
        private volatile Built? _built;

        public Built? Current => _built;

        public void Store(Built built) => _built = built;
    }

    /// <summary>A context built from <paramref name="Stamp"/>'s days; <paramref name="Context"/> is null when the library refused a row, <paramref name="First"/> the oldest date.</summary>
    internal sealed record Built(ContextStamp Stamp, MarketContext? Context, DateOnly? First);

    public async ValueTask<ContextRead> AsOfAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var built = await BuiltAsync(cancellationToken);
        if (built.Stamp.Count == 0)
        {
            return ContextRead.Not(ContextRead.NoContext);
        }

        if (built.Context is not { } context)
        {
            return ContextRead.Not(ContextRead.Invalid);
        }

        if (context.TryAsOf(date, out var values))
        {
            return ContextRead.Of(Figures(values));
        }

        return ContextRead.Not(built.First is { } first && date <= first ? ContextRead.BeforeHistory : ContextRead.Stale);
    }

    /// <summary>The ten values with the day they belong to; a value that is not ready is null. The basis says whether the VIX percentile was part of the regime (D2).</summary>
    internal static ContextFigures Figures(ContextValues v) => new(
        v.Date,
        double.IsNaN(v.VixPct) ? ContextBasis.IndexOnly : ContextBasis.IndexAndVix,
        Value(v.IndexTrend),
        Value(v.IndexAdx),
        Value(v.IndexVolPct),
        Value(v.Vix),
        Value(v.VixPct),
        Value(v.VixChg),
        Value(v.RegimeTrend),
        Value(v.RegimeVol),
        Value(v.Regime),
        Value(v.Breadth));

    /// <summary>The context the stored days make, for a test that compares it with one built from the same records.</summary>
    internal static MarketContext? Build(IEnumerable<ContextDayRecord> records)
    {
        var days = records.Select(r => new ContextDay(r.Date, r.IndexHigh, r.IndexLow, r.IndexClose, r.VixClose));
        var result = MarketContext.Create(days);
        return result.IsSuccess ? result.Value : null;
    }

    private static double? Value(double value) => double.IsNaN(value) ? null : value;

    private async ValueTask<Built> BuiltAsync(CancellationToken cancellationToken)
    {
        var stamp = await store.StampAsync(cancellationToken);
        if (cache.Current is { } current && current.Stamp == stamp)
        {
            return current;
        }

        var records = await store.ListAsync(cancellationToken);
        var built = new Built(new ContextStamp(records.Count, records.Count > 0 ? records[^1].Date : null), records.Count == 0 ? null : Build(records), records.Count > 0 ? records[0].Date : null);
        cache.Store(built);
        return built;
    }
}
