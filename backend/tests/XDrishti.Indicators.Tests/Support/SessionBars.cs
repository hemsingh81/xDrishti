using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// The rule of ADR 0035 written again from its definition, for the tests that compare a frame with the reference indicators: of the twelve per-session features on an intraday timeframe, only the bars the timeframe has closed
/// <i>in the date of the minute just fed</i> are known. <see cref="Known"/> counts them (<i>S</i>): the closed buckets, newest first, whose start lies on that India date (<see cref="SessionClock.DateOf"/> of the Domain, not the
/// library's own date arithmetic); 0 when the newest closed bucket is of an earlier date (a coarser lane that has not closed its first bar of the date, or a bucket that closed late). A read <c>k</c> bars back is known exactly
/// when <c>k &lt; S</c>, and then it has the value of the reference series; otherwise it is unknown. Nothing here reads a frame or <see cref="IndicatorInfo"/>.
/// </summary>
internal sealed class SessionBars
{
    /// <summary>The twelve features that restart (written out; not derived from the catalog).</summary>
    public static readonly string[] RestartingNames =
    [
        "vwap", "or_high", "or_low", "today_open", "today_high", "today_low", "prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "gap_pct",
    ];

    private DateOnly? _lastDate;

    private int _run;

    /// <summary>Whether the rule applies to the spec: one of the twelve names on a timeframe with minutes (1m to 1h); on 1D, 1W and 1M nothing restarts.</summary>
    public static bool Restarts(FeatureSpec spec) => RestartingNames.Contains(spec.Name) && spec.Timeframe.Minutes() > 0;

    /// <summary>Records a bucket the timeframe has closed (in closing order).</summary>
    public void Closed(ReferenceBucket bucket)
    {
        var date = SessionClock.DateOf(bucket.Start);
        if (_lastDate == date)
        {
            _run++;
        }
        else
        {
            _lastDate = date;
            _run = 1;
        }
    }

    /// <summary>How many bars of the date of the minute just fed the timeframe has closed.</summary>
    public int Known(DateOnly minuteDate) => _lastDate == minuteDate ? _run : 0;
}
