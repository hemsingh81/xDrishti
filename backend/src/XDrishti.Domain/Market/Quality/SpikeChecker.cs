using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// Spikes: the close-to-close move of consecutive bars of the same session, as a fraction of the previous close. A move greater than <c>k</c> times the median move of the window and greater than
/// 1 % is excessive. When the next bar takes back at least half of it, it is a bad tick (a <b>spike</b>, a warning); otherwise it is a real jump (a <b>large move</b>, only reported). The bar that
/// takes the spike back is not reported again. Overnight gaps (the first bar of a session) are never compared.
/// </summary>
public static class SpikeChecker
{
    public const int MaxExamples = 10;

    private const double RetraceFraction = 0.5;

    public static SpikeCheck Check(IReadOnlyList<Bar> windowBars, double spikeMultiple)
    {
        ArgumentNullException.ThrowIfNull(windowBars);
        var moves = new List<double>(windowBars.Count);
        for (var i = 1; i < windowBars.Count; i++)
        {
            if (SameSession(windowBars[i - 1], windowBars[i]) && windowBars[i - 1].Close > 0)
            {
                moves.Add(Math.Abs(windowBars[i].Close - windowBars[i - 1].Close) / windowBars[i - 1].Close);
            }
        }

        var median = Median(moves);
        var threshold = Math.Max(spikeMultiple * median, QualityThresholds.MinSpikeMove);
        int spikes = 0, large = 0;
        var spikeExamples = new List<SpikeExample>();
        var largeExamples = new List<SpikeExample>();
        var skipRetrace = false;
        for (var i = 1; i < windowBars.Count; i++)
        {
            var previous = windowBars[i - 1];
            var bar = windowBars[i];
            if (!SameSession(previous, bar) || previous.Close <= 0)
            {
                skipRetrace = false;
                continue;
            }

            var change = bar.Close - previous.Close;
            var move = Math.Abs(change) / previous.Close;
            if (skipRetrace)
            {
                skipRetrace = false;
                if (move > threshold)
                {
                    continue; // the leg that undoes the spike before it
                }
            }

            if (move <= threshold)
            {
                continue;
            }

            var reversed = i + 1 < windowBars.Count
                && SameSession(bar, windowBars[i + 1])
                && Math.Abs(windowBars[i + 1].Close - previous.Close) <= RetraceFraction * Math.Abs(change);
            var example = new SpikeExample(IndiaTime.ToIst(bar.Start), Math.Round(change / previous.Close * 100, 2), reversed);
            if (reversed)
            {
                spikes++;
                skipRetrace = true;
                AddExample(spikeExamples, example);
            }
            else
            {
                large++;
                AddExample(largeExamples, example);
            }
        }

        return new SpikeCheck(
            spikes == 0 ? CheckStatus.Pass : CheckStatus.Warning,
            spikes,
            large,
            Math.Round(median * 100, 4),
            Math.Round(threshold * 100, 4),
            [.. spikeExamples, .. largeExamples]);
    }

    private static void AddExample(List<SpikeExample> list, SpikeExample example)
    {
        if (list.Count < MaxExamples)
        {
            list.Add(example);
        }
    }

    private static bool SameSession(in Bar a, in Bar b) => SessionClock.DateOf(a.EpochSeconds) == SessionClock.DateOf(b.EpochSeconds);

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }
}
