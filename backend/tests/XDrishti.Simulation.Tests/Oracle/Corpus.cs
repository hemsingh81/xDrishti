namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The comparison runs of P3-08, made once and shared by the tests that read them (the walker comparison, the gate, the replay level, the coverage floors, the log): every generator family walked by the oracle and by the engine, in both fill
/// modes. A run is a pure function of the seeds, so two runs give the same summary; the series are compared in parallel and folded in a fixed order, so the printed lists are the same on every run.
/// <para>
/// <see cref="SeriesPerCategory"/> is the count of the default check (the floors of D7 are reached from <c>OracleSeries</c> alone: see <c>CoverageTests</c>); the explicit larger runs of the spec (5,000 per category) are
/// <c>LargeRunTests</c> and stay out of the default check.
/// </para>
/// </summary>
internal static class Corpus
{
    /// <summary>
    /// The series of O's generator per category in the default run (seeds 0 to n - 1). More than the 500 of the floor where a finer sub-case is only a part of its category (a Friday-to-Monday order is a fifth of category 14, a tie on the target
    /// a fifth of category 10, an end of data inside an intraday position a quarter of category 13, half of categories 9, 12 and 16 are one of two sub-cases): the numbers are the smallest that put each sub-case over 500 by the oracle's trace,
    /// measured on 2026-10-19 (<c>CoverageTests</c> asserts them).
    /// </summary>
    internal static readonly int[] SeriesPerCategory = [0, 600, 600, 600, 600, 600, 600, 600, 600, 1100, 2600, 600, 1100, 2200, 2600, 600, 1100];

    /// <summary>The ratio fields seen by all the runs below.</summary>
    internal static readonly RatioStats Stats = new();

    private static readonly Lazy<Tally> OracleLazy = new(() => RunOracleSeries(category => SeriesPerCategory[category]));

    private static readonly Lazy<Tally> IntradayLazy = new(() => Run(ComparisonCases.IntradayName, ComparisonCases.Intraday(exact: false).Concat(ComparisonCases.Intraday(exact: true))));

    private static readonly Lazy<Tally> DailyLazy = new(() => Run(ComparisonCases.DailyName, ComparisonCases.Daily(exact: false).Concat(ComparisonCases.Daily(exact: true))));

    private static readonly Lazy<Tally> SwingLazy = new(() => Run(ComparisonCases.SwingName, ComparisonCases.Swing(exact: false).Concat(ComparisonCases.Swing(exact: true))));

    private static readonly Lazy<IReadOnlyList<Tally>> ReplayLazy = new(() =>
    [
        .. ComparisonCases.ReplayStrategies.Select(s => Run(ComparisonCases.ReplayName + ":" + s.Name, ComparisonCases.Replay(exact: false, s.Name).Concat(ComparisonCases.Replay(exact: true, s.Name)))),
    ]);

    /// <summary>O's own generator: 16 categories, <see cref="SeriesPerCategory"/> series each.</summary>
    internal static Tally OracleSeriesRun => OracleLazy.Value;

    internal static Tally IntradayRun => IntradayLazy.Value;

    internal static Tally DailyRun => DailyLazy.Value;

    internal static Tally SwingRun => SwingLazy.Value;

    /// <summary>The 100 replay series for each of three strategies (an intraday one, a swing on the 1h bar, a swing on the daily bar).</summary>
    internal static IReadOnlyList<Tally> ReplayRuns => ReplayLazy.Value;

    /// <summary>Every run of the default check, in the order of the printed summary.</summary>
    internal static IReadOnlyList<Tally> All => [OracleSeriesRun, IntradayRun, DailyRun, SwingRun, .. ReplayRuns];

    /// <summary>Runs O's generator for the seeds 0 to <paramref name="count"/> - 1 of every category (the count may depend on the category), in both fill modes.</summary>
    internal static Tally RunOracleSeries(Func<int, int> count)
    {
        var results = new SeriesResult[OracleCategories.Count][];
        Parallel.For(0, OracleCategories.Count, index =>
        {
            var category = index + 1;
            var list = new List<SeriesResult>();
            for (long seed = 0; seed < count(category); seed++)
            {
                var source = OracleSeries.Of(category, seed);
                list.Add(SeriesComparison.Run(ComparisonCases.OfOracle(source, exact: false), Stats));
                list.Add(SeriesComparison.Run(ComparisonCases.OfOracle(source, exact: true), Stats));
            }

            results[index] = [.. list];
        });
        var tally = new Tally(ComparisonCases.OracleSeriesName);
        foreach (var result in results.SelectMany(r => r))
        {
            tally.Add(result);
        }

        return tally;
    }

    private static Tally Run(string name, IEnumerable<ComparisonCase> cases)
    {
        var results = cases.AsParallel().AsOrdered().Select(c => SeriesComparison.Run(c, Stats)).ToList();
        var tally = new Tally(name);
        foreach (var result in results)
        {
            tally.Add(result);
        }

        return tally;
    }
}
