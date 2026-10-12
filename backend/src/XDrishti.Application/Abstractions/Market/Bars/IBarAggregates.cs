namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>An instrument whose 1-minute bars changed in [<see cref="From"/>, <see cref="To"/>] (bar starts) since its aggregates were built.</summary>
public sealed record DirtyInstrument(Guid InstrumentId, string Symbol, DateTimeOffset From, DateTimeOffset To);

/// <summary>How many bars (new or changed) a build wrote, by timeframe code.</summary>
public sealed record AggregatesBuilt(IReadOnlyDictionary<string, long> BucketsByTimeframe);

/// <summary>The derived timeframes (5m … 1M; ADR 0016 §6), built from the stored 1-minute bars by set-based upserts.</summary>
public interface IBarAggregates
{
    /// <summary>Marks every instrument with bars as changed over its whole range (a full rebuild); returns how many.</summary>
    ValueTask<int> MarkAllDirtyAsync(CancellationToken cancellationToken);

    /// <summary>Marks the instrument's bars from <paramref name="from"/> to its last bar as changed (widening a range already marked), so the next build rebuilds them; false when it has no bars.</summary>
    ValueTask<bool> MarkDirtyAsync(Guid instrumentId, DateTimeOffset from, CancellationToken cancellationToken);

    /// <summary>The instruments with changes not aggregated yet, by symbol.</summary>
    ValueTask<IReadOnlyList<DirtyInstrument>> ListDirtyAsync(CancellationToken cancellationToken);

    /// <summary>Builds every derived timeframe of the instrument for the buckets that contain the changed range, refreshes its coverage and clears its changed range, in one transaction.</summary>
    ValueTask<AggregatesBuilt> BuildAsync(DirtyInstrument target, CancellationToken cancellationToken);
}
