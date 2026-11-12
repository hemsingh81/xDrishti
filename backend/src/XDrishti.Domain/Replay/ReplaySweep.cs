using System.Globalization;

namespace XDrishti.Domain.Replay;

/// <summary>One finished start of a sweep: the figures the spread is made of. A figure that cannot be told is null.</summary>
public readonly record struct ReplaySweepPoint(double? Cagr, decimal? MaxDrawdownPct, bool? BeatsBenchmark);

/// <summary>The least, the middle and the greatest of a figure over the starts.</summary>
public sealed record SweepSpread(double Min, double Median, double Max);

/// <summary>The spread of a sweep (SR-02, D3): min, median and max of CAGR and of max drawdown, the share of starts with a CAGR above 0 and the share that beat the benchmark. Each is null where no start gave the figure.</summary>
public sealed record ReplaySweepSpread(int Starts, SweepSpread? Cagr, SweepSpread? MaxDrawdownPct, double? ShareCagrPositive, double? ShareBeatingBenchmark);

/// <summary>The rules of a start-date sweep and its spread. Pure.</summary>
public static class ReplaySweepRules
{
    /// <summary>The fewest start dates of a sweep.</summary>
    public const int MinStarts = 2;

    /// <summary>The most start dates of a sweep.</summary>
    public const int MaxStarts = 12;

    /// <summary>The fewest trading sessions between a start and the shared end (the start included).</summary>
    public const int MinSessions = 126;

    /// <summary>The problems of the list of starts (count, duplicates, a start not before the end); empty when usable. The session count and the cell statistics are checked per start where they are read.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<DateOnly>? starts, DateOnly end)
    {
        var p = new List<string>();
        if (starts is null || starts.Count is < MinStarts or > MaxStarts)
        {
            p.Add(string.Create(CultureInfo.InvariantCulture, $"a sweep has {MinStarts} to {MaxStarts} start dates"));
            return p;
        }

        if (starts.Distinct().Count() != starts.Count)
        {
            p.Add("the start dates must differ");
        }

        foreach (var s in starts.Where(s => s >= end))
        {
            p.Add(string.Create(CultureInfo.InvariantCulture, $"start {s:yyyy-MM-dd} is not before the end {end:yyyy-MM-dd}"));
        }

        return p;
    }

    /// <summary>The spread over the finished starts.</summary>
    public static ReplaySweepSpread Spread(IReadOnlyList<ReplaySweepPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var cagr = points.Where(p => p.Cagr is not null).Select(p => p.Cagr!.Value).ToList();
        var dd = points.Where(p => p.MaxDrawdownPct is not null).Select(p => (double)p.MaxDrawdownPct!.Value).ToList();
        var beats = points.Where(p => p.BeatsBenchmark is not null).ToList();
        return new ReplaySweepSpread(
            points.Count,
            Of(cagr),
            Of(dd),
            cagr.Count == 0 ? null : cagr.Count(c => c > 0) / (double)cagr.Count,
            beats.Count == 0 ? null : beats.Count(b => b.BeatsBenchmark == true) / (double)beats.Count);
    }

    private static SweepSpread? Of(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        var mid = values.Count / 2;
        var median = values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
        return new SweepSpread(values[0], median, values[^1]);
    }
}
