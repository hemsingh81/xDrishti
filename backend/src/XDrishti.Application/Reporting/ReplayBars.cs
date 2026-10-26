using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Reporting;

/// <summary>Adjusts stored 1-minute bars by the factors of the instrument (the rule of the replay: the factor of each bar's India date) and builds the bars of the chosen timeframe with the domain aggregator, so that no bar is computed in the browser (P8-11, D3).</summary>
internal static class ReplayBars
{
    public static List<Bar> Adjust(IReadOnlyList<Bar> raw, AdjustmentFactors factors)
    {
        var result = new List<Bar>(raw.Count);
        var date = default(DateOnly);
        var factor = 1.0;
        var haveDate = false;
        foreach (var bar in raw)
        {
            var d = SessionClock.DateOf(bar.EpochSeconds);
            if (!haveDate || d != date)
            {
                date = d;
                haveDate = true;
                factor = factors.FactorAt(d);
            }

            result.Add(factor == 1.0
                ? bar
                : new Bar(bar.EpochSeconds, AdjustmentFactors.Price(bar.Open, factor), AdjustmentFactors.Price(bar.High, factor), AdjustmentFactors.Price(bar.Low, factor), AdjustmentFactors.Price(bar.Close, factor), AdjustmentFactors.Volume(bar.Volume, factor)));
        }

        return result;
    }

    /// <summary>The bars of the timeframe, oldest first: the minutes as they are for 1m, else the buckets of <see cref="MinuteAggregator"/>.</summary>
    public static List<ReplayBarView> Aggregate(IReadOnlyList<Bar> minutes, Timeframe timeframe)
    {
        if (timeframe == Timeframe.M1)
        {
            return [.. minutes.Select(b => new ReplayBarView(b.EpochSeconds, b.Open, b.High, b.Low, b.Close, b.Volume))];
        }

        return [.. MinuteAggregator.Build(minutes)
            .Where(kv => kv.Key.Timeframe == timeframe)
            .OrderBy(kv => kv.Key.Start)
            .Select(kv => new ReplayBarView(kv.Key.Start, kv.Value.Open, kv.Value.High, kv.Value.Low, kv.Value.Close, kv.Value.Volume))];
    }
}
