namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The invariant -MAE &lt;= RExact &lt;= MFE over the oracle's series and the engine's seeded days, daily and swing series, with no corner excluded: log entry Q3-MFE (ISS-28) was decided in P4-01 (sim-6), the favourable extreme reaches at
/// least the close of the last minute, so an entry at the limit whose fill minute is also the exit minute by EndOfData or MaxHold satisfies it too. The two planted series reach that corner, which the generators do not.
/// </summary>
public sealed class ExcursionInvariantTests(ITestOutputHelper output)
{
    private const double Slack = 1e-9;

    private static (int Checked, int Excluded, List<string> Bad) Scan(IEnumerable<OracleResult> results, string label)
    {
        int checkedCount = 0;
        var bad = new List<string>();
        foreach (var t in results.Select(r => r.Trade).OfType<OracleTrade>())
        {
            checkedCount++;
            if (-t.MaeR > t.RExact + Slack || t.RExact > t.MfeR + Slack)
            {
                bad.Add($"{label}: {t.ExitReason} R {t.RExact} MAE {t.MaeR} MFE {t.MfeR}");
            }
        }

        return (checkedCount, 0, bad);
    }

    private static IEnumerable<OracleResult> Engine(IEnumerable<ComparisonCase> cases) =>
        cases.SelectMany(c => c.Groups.SelectMany(g => OracleAdapter.Walk(c.Bars, g.EngineCandidates, g.EngineRules).Results));

    /// <summary>Two planted series that reach the corner (the generators do not): the end of the data at the fill minute, and the due minute of a one-session hold at the fill minute.</summary>
    private static IEnumerable<OracleResult> Planted()
    {
        Tape end = new Tape().Day(Fx.Mon, "09:15", "10:00", 100.00m, [Fx.M("10:00", 100.30m, 100.60m, 100.05m, 100.20m)]);
        Tape due = new Tape().Day(Fx.Mon, "09:15", "15:29", 100.00m, [Fx.M("15:29", 100.30m, 100.60m, 100.05m, 100.20m)]);
        yield return Corner(end, "10:00", Fx.Intraday(OracleSlippage.None));
        yield return Corner(due, "15:29", Fx.Swing(1, slip: OracleSlippage.None));
    }

    private static OracleResult Corner(Tape tape, string decision, OracleRules rules) =>
        OracleAdapter.Walk(OracleAdapter.Bars(tape.ToArray()), [OracleAdapter.Candidate(Fx.Cand(tape, OracleSide.Buy, Fx.Mon, decision, 100.00m, 99.00m))], OracleAdapter.Rules(rules)).Results[0];

    [Fact]
    public void The_excursions_bracket_the_result_everywhere_the_planted_corner_included()
    {
        var oracle = Enumerable.Range(1, OracleCategories.Count)
            .SelectMany(c => Enumerable.Range(0, 300).Select(s => ComparisonCases.OfOracle(OracleSeries.Of(c, s), exact: false)));
        var families = new (string Name, IEnumerable<ComparisonCase> Cases)[]
        {
            ("OracleSeries", oracle),
            ("SeededDays", ComparisonCases.Intraday(exact: false)),
            ("SeededDaily", ComparisonCases.Daily(exact: false)),
            ("SwingSeries", ComparisonCases.Swing(exact: false)),
        };

        int total = 0;
        var bad = new List<string>();
        var planted = Scan(Planted(), "planted corner");
        output.WriteLine($"planted corner: {planted.Checked} checked, {planted.Excluded} excluded, {planted.Bad.Count} violations (the generators of all four families never reach the corner: 0 excluded above)");
        bad.AddRange(planted.Bad);
        foreach (var (name, cases) in families)
        {
            var (n, ex, b) = Scan(Engine(cases), name);
            output.WriteLine($"{name}: {n} trades checked, {ex} excluded as the Q3 corner, {b.Count} violations");
            total += n;
            bad.AddRange(b);
        }

        bad.Take(10).ShouldBeEmpty();
        total.ShouldBeGreaterThan(1000);
        planted.Checked.ShouldBe(2, "both planted series reach the corner and are checked");
    }

    [Fact]
    public void The_corner_that_broke_the_invariant_in_sim_5_satisfies_it_in_sim_6()
    {
        Tape t = new Tape().Day(Fx.Mon, "09:15", "10:00", 100.00m, [Fx.M("10:00", 100.30m, 100.60m, 100.05m, 100.20m)]);
        var c = Fx.Cand(t, OracleSide.Buy, Fx.Mon, "10:00", 100.00m, 99.00m);
        var r = OracleAdapter.Walk(OracleAdapter.Bars(t.ToArray()), [OracleAdapter.Candidate(c)], OracleAdapter.Rules(Fx.Intraday(OracleSlippage.None))).Results[0];

        r.Trade!.EntryFill.ShouldBe(OracleEntryFill.AtLimit);
        r.Trade.MfeR.ShouldBeGreaterThanOrEqualTo(r.Trade.RExact);
    }
}
