using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>What the frame must have closed at each step, according to the independent grouping reference: for a timeframe, the buckets that close at every step in the order they close.</summary>
internal static class FrameReference
{
    /// <summary>For each step, the buckets closed by it (none, one, two). 1m closes the bar itself at every step.</summary>
    public static List<ReferenceBucket>[] ClosedByStep(IReadOnlyList<Bar> bars, Timeframe timeframe, IReadOnlyDictionary<DateOnly, (int From, int To)>? sessions = null)
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

        foreach (var bucket in ReferenceBuckets.Build(bars, timeframe, sessions).Where(b => b.CloseIndex != int.MaxValue).OrderBy(b => b.CloseIndex).ThenBy(b => b.Start))
        {
            byStep[bucket.CloseIndex].Add(bucket);
        }

        return byStep;
    }

    /// <summary>The closed bars of the timeframe in closing order (what an indicator on that timeframe is fed).</summary>
    public static List<ReferenceBucket> ClosedInOrder(IReadOnlyList<Bar> bars, Timeframe timeframe, IReadOnlyDictionary<DateOnly, (int From, int To)>? sessions = null) =>
        [.. ClosedByStep(bars, timeframe, sessions).SelectMany(x => x)];
}
