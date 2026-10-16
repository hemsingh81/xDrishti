using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// What the frame must have closed at each step, according to the independent grouping reference: for a timeframe, the buckets that close at every step in the order they close. The weekly and monthly ones come from
/// <see cref="ReferenceBuckets.BuildPeriods"/> (the plain grouping by Monday or by the 1st, closed at the last session with a calendar and at the first bar of the next period without).
/// </summary>
internal static class FrameReference
{
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    /// <summary>
    /// For each step, the buckets closed by it (none, one, two). 1m closes the bar itself at every step. <paramref name="sessions"/> are the special sessions the calendar knows (null: no calendar); for 1W and 1M
    /// <paramref name="holidays"/> are the weekday holidays it knows as well (they do not matter below a week).
    /// </summary>
    public static List<ReferenceBucket>[] ClosedByStep(IReadOnlyList<Bar> bars, Timeframe timeframe, IReadOnlyDictionary<DateOnly, (int From, int To)>? sessions = null, IReadOnlySet<DateOnly>? holidays = null)
    {
        var byStep = new List<ReferenceBucket>[bars.Count];
        for (var t = 0; t < bars.Count; t++)
        {
            byStep[t] = [];
        }

        if (timeframe == Timeframe.M1)
        {
            for (var t = 0; t < bars.Count; t++)
            {
                var b = bars[t];
                byStep[t].Add(new ReferenceBucket(b.EpochSeconds, b.Open, b.High, b.Low, b.Close, b.Volume, 1, true, t));
            }

            return byStep;
        }

        var buckets = timeframe is Timeframe.W1 or Timeframe.Mo1
            ? ReferenceBuckets.BuildPeriods(bars, timeframe, sessions is null ? null : new ReferenceCalendar(sessions, holidays ?? NoHolidays)).Select(p => p.Bucket)
            : ReferenceBuckets.Build(bars, timeframe, sessions);
        foreach (var bucket in buckets.Where(b => b.CloseIndex != int.MaxValue).OrderBy(b => b.CloseIndex).ThenBy(b => b.Start))
        {
            byStep[bucket.CloseIndex].Add(bucket);
        }

        return byStep;
    }

    /// <summary>The closed bars of the timeframe in closing order (what an indicator on that timeframe is fed).</summary>
    public static List<ReferenceBucket> ClosedInOrder(IReadOnlyList<Bar> bars, Timeframe timeframe, IReadOnlyDictionary<DateOnly, (int From, int To)>? sessions = null, IReadOnlySet<DateOnly>? holidays = null) =>
        [.. ClosedByStep(bars, timeframe, sessions, holidays).SelectMany(x => x)];
}
