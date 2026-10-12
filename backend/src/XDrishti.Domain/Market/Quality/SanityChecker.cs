using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.Quality;

/// <summary>Bar sanity: every stored bar of the window must satisfy the row rules of the load (low ≤ open and close ≤ high, positive finite prices, volume not negative). Any bad bar fails the instrument.</summary>
public static class SanityChecker
{
    public const int MaxExamples = 10;

    public static SanityCheck Check(IReadOnlyList<Bar> windowBars)
    {
        ArgumentNullException.ThrowIfNull(windowBars);
        var bad = 0;
        var examples = new List<BadBar>();
        foreach (var bar in windowBars)
        {
            if (Problem(bar) is not { } problem)
            {
                continue;
            }

            bad++;
            if (examples.Count < MaxExamples)
            {
                examples.Add(new BadBar(IndiaTime.ToIst(bar.Start), problem));
            }
        }

        return new SanityCheck(bad == 0 ? CheckStatus.Pass : CheckStatus.Fail, windowBars.Count, bad, examples);
    }

    /// <summary>What is wrong with the bar, or null when nothing is.</summary>
    public static string? Problem(in Bar bar)
    {
        if (!double.IsFinite(bar.Open) || !double.IsFinite(bar.High) || !double.IsFinite(bar.Low) || !double.IsFinite(bar.Close))
        {
            return "a price is not a number";
        }

        if (bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0)
        {
            return "a price is not positive";
        }

        if (bar.Low > bar.Open || bar.Low > bar.Close || bar.Low > bar.High)
        {
            return "the low is above the open, close or high";
        }

        if (bar.High < bar.Open || bar.High < bar.Close)
        {
            return "the high is below the open or close";
        }

        return bar.Volume < 0 ? "the volume is negative" : null;
    }
}
