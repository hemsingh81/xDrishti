using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>Builds the excursion counts of one group in one pass over its rows (spec P8-10). Bins are fixed here so the answer is small and deterministic.</summary>
internal static class MaeMfeBuilder
{
    private const double MaeWidth = 0.25;
    private const double MaeTop = 2;
    private const double ResultWidth = 0.5;
    private const double ResultLimit = 5;
    private const double MfeWidth = 0.5;
    private const double MfeTop = 5;

    private static readonly double[] Levels = [1, 2, 3];

    public static (MaeResultGrid Grid, MfeHistogram Mfe, ValueList<ReachedLevel> Reached, int N, Metric WinnersMaeP90) Build(IReadOnlyList<ReportRow> rows, int minWins)
    {
        var maeEdges = Edges(0, MaeTop, MaeWidth);
        var resultEdges = Edges(-ResultLimit, ResultLimit, ResultWidth);
        var mfeEdges = Edges(0, MfeTop, MfeWidth);
        var grid = new int[maeEdges.Count][];
        for (var i = 0; i < grid.Length; i++)
        {
            grid[i] = new int[resultEdges.Count + 1];
        }

        var mfe = new int[mfeEdges.Count - 1];
        var mfeAbove = 0;
        var reached = new int[Levels.Length];
        var winnerMae = new List<double>();
        var n = 0;
        foreach (var row in rows)
        {
            if (row.Trade.MaeR is not { } mae || row.Trade.MfeR is not { } fav)
            {
                continue;
            }

            n++;
            var m = Math.Min((int)Math.Floor(mae / MaeWidth), maeEdges.Count - 1);
            var r = row.R < resultEdges[0] ? 0 : Math.Min((int)Math.Floor((row.R - resultEdges[0]) / ResultWidth) + 1, resultEdges.Count);
            grid[Math.Max(m, 0)][r]++;
            var f = (int)Math.Floor(fav / MfeWidth);
            if (f >= mfe.Length)
            {
                mfeAbove++;
            }
            else
            {
                mfe[Math.Max(f, 0)]++;
            }

            for (var i = 0; i < Levels.Length; i++)
            {
                if (fav >= Levels[i])
                {
                    reached[i]++;
                }
            }

            if (row.R > 0)
            {
                winnerMae.Add(mae);
            }
        }

        var below = 0;
        var above = 0;
        foreach (var line in grid)
        {
            below += line[0];
            above += line[^1];
        }

        winnerMae.Sort();
        var p90 = winnerMae.Count < minWins
            ? Metric.Insufficient($"{winnerMae.Count} winning {(winnerMae.Count == 1 ? "trade" : "trades")} with a recorded excursion, at least {minWins} needed")
            : Metric.Of(winnerMae[(int)Math.Ceiling((0.9 * winnerMae.Count) - 1e-9) - 1]);
        return (
            new MaeResultGrid(new ValueList<double>(maeEdges), new ValueList<double>(resultEdges), new ValueList<ValueList<int>>(grid.Select(l => new ValueList<int>(l))), below, above),
            new MfeHistogram(new ValueList<double>(mfeEdges), new ValueList<int>(mfe), mfeAbove),
            new ValueList<ReachedLevel>(Levels.Select((l, i) => new ReachedLevel(l, reached[i], n == 0 ? 0 : (double)reached[i] / n))),
            n,
            p90);
    }

    private static List<double> Edges(double from, double to, double width)
    {
        var edges = new List<double>();
        for (var i = 0; from + (i * width) < to + 1e-9; i++)
        {
            edges.Add(from + (i * width));
        }

        return edges;
    }
}
