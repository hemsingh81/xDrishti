using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The series the comparison walks (spec P3-08 D3): the oracle author's <c>OracleSeries</c>, taken to the engine through the adapter, and the engine's own seeded generators (<c>SeededDays</c>, <c>SeededDaily</c>, <c>SwingSeries</c> and the
/// replay series), taken to the oracle. Each series is compared in two fill modes: the modelled one (the default slippage) and the exact one (no slippage: every filled price is the exact price), so that a rule about slippage and a rule
/// about the exact fill are both read.
/// </summary>
internal static class ComparisonCases
{
    internal const string OracleSeriesName = "OracleSeries";

    internal const string IntradayName = "SeededDays";

    internal const string DailyName = "SeededDaily";

    internal const string SwingName = "SwingSeries";

    internal const string ReplayName = "SeededReplaySeries";

    internal static string Mode(bool exact) => exact ? "exact" : "modelled";

    private static FillRules Fills(bool exact) => exact ? Cand.NoSlippage : FillRules.Default;

    /// <summary>An oracle series, with the engine's inputs derived by the adapter.</summary>
    internal static ComparisonCase OfOracle(OracleCase source, bool exact)
    {
        var rules = exact ? source.Rules with { Slippage = OracleSlippage.None } : source.Rules;
        var oracle = source with { Rules = rules };
        var group = new CaseGroup([.. source.Candidates.Select(OracleAdapter.Candidate)], OracleAdapter.Rules(rules), oracle);
        return new ComparisonCase(OracleSeriesName, source.Seed, source.Category, Mode(exact), OracleAdapter.Bars(source.Bars), [group]);
    }

    /// <summary>A group of an engine series: the engine's candidates and rules, with the oracle's derived by the adapter.</summary>
    internal static CaseGroup GroupOfEngine(long seed, IReadOnlyList<Bar> bars, IReadOnlyList<EntryCandidate> candidates, WalkRules rules, IReadOnlyList<WalkOutcome>? results = null, IReadOnlyList<Bar>? oracleBars = null)
    {
        var oracleSource = oracleBars ?? bars;
        var oracle = new OracleCase(0, seed, OracleAdapter.Bars(oracleSource), [.. candidates.Select(OracleAdapter.Candidate)], OracleAdapter.Rules(rules, oracleSource));
        return new CaseGroup(candidates, rules, oracle, results);
    }

    /// <summary>The 500 seeded intraday days: each candidate has its own reward-to-risk, so each is a group of one (an intraday walk has no gate).</summary>
    internal static IEnumerable<ComparisonCase> Intraday(bool exact) =>
        SeededDays.All.Select(day => new ComparisonCase(
            IntradayName,
            day.Seed,
            0,
            Mode(exact),
            day.Bars,
            [.. day.Walks.Select(w => GroupOfEngine(day.Seed, day.Bars, [w.Candidate], w.Rules with { Fills = Fills(exact) }))]));

    /// <summary>The 500 seeded daily series (decisions at 15:30, orders that rest overnight, through the gate).</summary>
    internal static IEnumerable<ComparisonCase> Daily(bool exact) =>
        SeededDaily.All.Select(series => new ComparisonCase(
            DailyName,
            series.Seed,
            0,
            Mode(exact),
            series.Bars,
            [GroupOfEngine(series.Seed, series.Bars, series.Candidates, series.Rules(Fills(exact)))]));

    /// <summary>The 500 seeded swing series (several sessions, holidays and special sessions, candidates inside the hold of the first).</summary>
    internal static IEnumerable<ComparisonCase> Swing(bool exact) =>
        SeededSwing.All.Select(series => new ComparisonCase(
            SwingName,
            series.Seed,
            0,
            Mode(exact),
            series.Bars,
            [GroupOfEngine(series.Seed, series.Bars, series.Candidates, series.Rules(Fills(exact)))]));

    /// <summary>The strategies the replay series run: the shapes of an intraday session, of a swing on the 1h bar and of a swing on the daily bar, with their walk rules.</summary>
    internal static IReadOnlyList<(string Name, Func<bool, PreparedStrategy> Prepare, Func<bool, WalkRules> Rules)> ReplayStrategies { get; } =
    [
        ("orb", exact => Rep.Prepare(OrbV1.Create(), Timeframe.M15, Fills(exact)), exact => new WalkRules(2.0, 915, Fills(exact), Sim.Tick)),
        ("swing-1h", exact => Rep.Prepare(TestStrategies.Swing1hEma(), Timeframe.H1, Fills(exact)), exact => new WalkRules(2.0, WalkRules.NoSquareOff, Fills(exact), Sim.Tick, SwingHold.Create(3, TestCalendars.DefaultWeek()).Value)),
        ("swing-daily", exact => Rep.Prepare(TestStrategies.SwingDaily(), Timeframe.D1, Fills(exact)), exact => new WalkRules(2.0, WalkRules.NoSquareOff, Fills(exact), Sim.Tick, SwingHold.Create(5, TestCalendars.DefaultWeek()).Value)),
    ];

    /// <summary>
    /// The 100 seeded replay series, through the real simulator for each strategy: the candidates it scanned and the outcomes it gave are read from the segments of the replay, and the oracle walks and gates the same candidates over the same
    /// minutes. This is the walk-and-gate level of criterion 7 (the window cut, the flagged-night exclusion and the fates of the run record are P3-04's, which the oracle's author was not given; see the log).
    /// </summary>
    internal static IEnumerable<ComparisonCase> Replay(bool exact, string strategy)
    {
        var entry = ReplayStrategies.Single(s => s.Name == strategy);
        var prepared = entry.Prepare(exact);
        var rules = entry.Rules(exact);
        foreach (var series in SeededReplay.All)
        {
            var (_, segments) = SeededReplay.Replay(prepared, series.Bars, series.Dates[0], series.Dates[^1]);
            var groups = segments
                .Where(s => s.Result.Outcomes.Count > 0)
                .Select(s => GroupOfEngine(series.Seed, series.Bars, [.. s.Result.Outcomes.Select(o => o.Candidate)], rules, [.. s.Result.Outcomes.Select(o => o.Walk)], s.List))
                .ToList();
            yield return new ComparisonCase(ReplayName + ":" + strategy, series.Seed, 0, Mode(exact), series.Bars, groups);
        }
    }
}
