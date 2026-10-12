using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>How many gaps are in each state and when one was last found (<c>lastDetectedAt</c>: null while there never was one).</summary>
public sealed record GapSummary(int Open, int Unfillable, int Filled, DateTimeOffset? LastDetectedAt);

/// <summary>The gaps of sessions (<c>market.bar_gaps</c>, ADR 0017 §3): the records of what the Dhan phase found missing and what became of it.</summary>
public interface IBarGaps
{
    /// <summary>The gaps of the instrument that are not filled yet (any date) and the filled ones from <paramref name="since"/> on.</summary>
    ValueTask<IReadOnlyList<BarGap>> ListForAsync(Guid instrumentId, DateOnly since, CancellationToken cancellationToken);

    /// <summary>Writes the gaps (new ones are added, known ones updated).</summary>
    ValueTask SaveAsync(IReadOnlyCollection<BarGap> gaps, CancellationToken cancellationToken);

    ValueTask<GapSummary> GetSummaryAsync(CancellationToken cancellationToken);

    ValueTask<GapPage> ListAsync(GapSearch search, CancellationToken cancellationToken);
}

/// <summary>
/// The number of stored 1-minute bars per India session day, cheaply: days whose daily aggregate is current come from the 1D aggregate's <c>bars</c> (which already counts
/// the 1-minute bars of the day), only the days with changes not aggregated yet (the instrument's dirty range) are counted in the 1-minute table. Never scans the 1-minute
/// table over the whole lookback.
/// </summary>
public interface IBarDayCounts
{
    /// <summary>The bars per day for the days from <paramref name="from"/> to <paramref name="to"/> (both included); a day without bars is not in the result.</summary>
    ValueTask<IReadOnlyDictionary<DateOnly, int>> CountAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
