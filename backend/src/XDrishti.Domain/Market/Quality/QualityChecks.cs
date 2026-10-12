namespace XDrishti.Domain.Market.Quality;

/// <summary>One trading session of the window as completeness sees it: the minutes the calendar expects, the ones stored, and whether a refetch already proved the day unfillable (<c>bar_gaps</c>).</summary>
public sealed record SessionCompleteness(DateOnly Date, int Expected, int Found, int Missing, bool Unfillable);

/// <summary>A run of missing minutes: <see cref="Minutes"/> consecutive minutes from <see cref="FromMinute"/> (minutes since India midnight) on <see cref="Date"/>.</summary>
public sealed record MissingRange(DateOnly Date, int FromMinute, int Minutes);

/// <summary>
/// Completeness: the 1-minute bars the trading calendar expects in the window against the ones stored. <c>missingPercent</c> is of the expected minutes; <c>unfillableMinutes</c> are missing
/// minutes of sessions a refetch could not fill (a halt, a circuit-locked day): they are a warning, never a failure.
/// </summary>
public sealed record CompletenessCheck(string Status, int ExpectedMinutes, int FoundMinutes, int MissingMinutes, double MissingPercent, int UnfillableMinutes, IReadOnlyList<SessionCompleteness> Sessions);

/// <summary>A bar that broke a row rule: its start and what is wrong with it.</summary>
public sealed record BadBar(DateTimeOffset Ts, string Problem);

/// <summary>Bar sanity: low ≤ open and close ≤ high, prices positive and finite, volume not negative. <c>examples</c> holds the first few bad bars.</summary>
public sealed record SanityCheck(string Status, int BarsChecked, int BadBars, IReadOnlyList<BadBar> Examples);

/// <summary>A close-to-close move that was far above the median: <c>movePercent</c> is signed; <c>reversed</c> says the next bar took at least half of it back (a bad tick, not a real move).</summary>
public sealed record SpikeExample(DateTimeOffset Ts, double MovePercent, bool Reversed);

/// <summary>
/// Spikes: a 1-minute move greater than <c>spikeMultiple</c> times the median move and greater than 1 %. One the next bar takes back (at least half) is a spike (a warning); one that is not
/// taken back is a <c>largeMove</c>, a real jump, only reported. <c>thresholdPercent</c> is the move a bar needed to count.
/// </summary>
public sealed record SpikeCheck(string Status, int Spikes, int LargeMoves, double MedianMovePercent, double ThresholdPercent, IReadOnlyList<SpikeExample> Examples);

/// <summary>The aggregates of one timeframe compared with the recomputed ones: <c>compared</c> buckets (stored or recomputed), <c>differences</c> among them.</summary>
public sealed record TimeframeCheck(string Status, int Compared, int Differences);

/// <summary>Aggregates against the 1-minute bars: every stored 5m, 10m, 15m, 30m, 1h, 1D, 1W and 1M bucket in the window recomputed independently and compared exactly (OHLC, volume, bar count).</summary>
public sealed record AggregatesCheck(string Status, int Compared, int Differences, IReadOnlyDictionary<string, TimeframeCheck> ByTimeframe);

/// <summary>Freshness against the trading calendar: the last bar and the last closed session; <c>daysBehind</c> counts trading days.</summary>
public sealed record FreshnessCheck(string Status, DateTimeOffset? LastBar, DateOnly ExpectedSession, int DaysBehind);

/// <summary>The comparison of the derived daily bar with the broker's official daily candle: not available until official daily candles are fetched.</summary>
public sealed record OfficialDailyCheck(string Status, string Message)
{
    public static OfficialDailyCheck NotAvailable { get; } = new(CheckStatus.NotAvailable, "Official daily candles are not fetched yet.");
}

/// <summary>A stored bucket that differs from the recomputed one: <c>fields</c> name what differs (open, high, low, close, volume, bars) or <c>missing</c> (not stored) / <c>unexpected</c> (stored, no 1-minute bars).</summary>
public sealed record BucketDifference(string Timeframe, DateTimeOffset Ts, IReadOnlyList<string> Fields);

/// <summary>The long lists behind the checks, kept apart so the list view can leave them out: the missing minutes as ranges and the differing buckets (capped).</summary>
public sealed record QualityDetails(IReadOnlyList<MissingRange> Missing, IReadOnlyList<BucketDifference> Differences);

/// <summary>Everything one run found about one instrument (stored as the result's <c>checks</c>).</summary>
public sealed record QualityChecks(
    CompletenessCheck Completeness,
    SanityCheck Sanity,
    SpikeCheck Spikes,
    AggregatesCheck Aggregates,
    FreshnessCheck Freshness,
    OfficialDailyCheck OfficialDaily,
    QualityDetails? Details);
