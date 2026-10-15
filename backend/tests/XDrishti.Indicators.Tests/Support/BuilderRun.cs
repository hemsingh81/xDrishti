using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>Runs a <see cref="TimeframeBuilder"/> over bars and records which step closed which bucket.</summary>
internal static class BuilderRun
{
    public static List<(int Step, ClosedBar Closed)> Run(IReadOnlyList<Bar> bars, Timeframe timeframe, TimeframeBuilder? builder = null, int firstStep = 0)
    {
        builder ??= new TimeframeBuilder(timeframe);
        var result = new List<(int, ClosedBar)>();
        for (var i = 0; i < bars.Count; i++)
        {
            var count = builder.Push(bars[i], out var first, out var second);
            if (count > 0)
            {
                result.Add((firstStep + i, first));
            }

            if (count > 1)
            {
                result.Add((firstStep + i, second));
            }
        }

        return result;
    }
}
