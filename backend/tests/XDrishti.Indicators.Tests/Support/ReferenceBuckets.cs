using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>A bucket as the grouping reference computes it. <see cref="CloseIndex"/> is the index of the 1-minute bar (in the input) whose arrival closes the bucket; <c>int.MaxValue</c> when no bar ever does.</summary>
internal sealed record ReferenceBucket(long Start, double Open, double High, double Low, double Close, long Volume, int Bars, bool Complete, int CloseIndex)
{
    public Bar AsBar => new(Start, Open, High, Low, Close, Volume);
}

/// <summary>
/// The independent grouping reference for the timeframe builder: the plain <c>GroupBy</c> of the stored-aggregate tests (<c>ReferenceAggregator</c>), written again here on India-time <see cref="DateTime"/> values,
/// plus the step at which a bucket closes: the bar of its last minute (09:15-anchored windows clipped at 15:30; for a day that starts in the regular session, the 15:29 bar), otherwise the next bar of the input.
/// </summary>
internal static class ReferenceBuckets
{
    private static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static List<ReferenceBucket> Build(IReadOnlyList<Bar> minuteBars, Timeframe timeframe)
    {
        var indexed = minuteBars.Select((bar, index) => (Bar: bar, Index: index)).ToList();
        var length = timeframe.Minutes();
        var groups = timeframe == Timeframe.D1
            ? indexed.GroupBy(x => Local(x.Bar.EpochSeconds).Date)
            : indexed.GroupBy(x => IntradayStart(Local(x.Bar.EpochSeconds), length));

        var result = new List<ReferenceBucket>();
        foreach (var group in groups.OrderBy(g => g.Key))
        {
            var items = group.OrderBy(x => x.Bar.EpochSeconds).ToList();
            var bars = items.Select(x => x.Bar).ToList();
            var startMinute = (int)(group.Key - group.Key.Date).TotalMinutes;
            bool complete;
            int windowEnd;
            if (timeframe == Timeframe.D1)
            {
                var regular = bars.Count(b => Minute(b) is >= 9 * 60 + 15 and < 15 * 60 + 30);
                var span = (bars[^1].EpochSeconds - bars[0].EpochSeconds) / 60 + 1;
                complete = regular == 375 || (regular == 0 && bars.Count == span);
                windowEnd = Minute(bars[0]) < 15 * 60 + 30 ? 15 * 60 + 30 : int.MaxValue;
            }
            else
            {
                var expected = startMinute < 15 * 60 + 30 ? Math.Min(length, 15 * 60 + 30 - startMinute) : length;
                complete = bars.Count == expected;
                windowEnd = startMinute + (startMinute < 15 * 60 + 30 ? expected : length);
            }

            var last = items[^1];
            var closeIndex = Minute(last.Bar) + 1 >= windowEnd ? last.Index : last.Index + 1 < minuteBars.Count ? last.Index + 1 : int.MaxValue;
            result.Add(new ReferenceBucket(
                ToEpoch(group.Key),
                bars[0].Open,
                bars.Max(b => b.High),
                bars.Min(b => b.Low),
                bars[^1].Close,
                bars.Sum(b => b.Volume),
                bars.Count,
                complete,
                closeIndex));
        }

        return result;
    }

    private static DateTime Local(long epochSeconds) => Epoch.AddSeconds(epochSeconds).Add(Offset);

    private static long ToEpoch(DateTime local) => (long)(local - Offset - Epoch).TotalSeconds;

    private static int Minute(Bar bar) => (int)(Local(bar.EpochSeconds) - Local(bar.EpochSeconds).Date).TotalMinutes;

    private static DateTime IntradayStart(DateTime local, int minutes)
    {
        var sinceOpen = (int)(local - local.Date).TotalMinutes - (9 * 60 + 15);
        var window = (int)Math.Floor(sinceOpen / (double)minutes);
        return local.Date.AddMinutes(9 * 60 + 15 + window * minutes);
    }
}
