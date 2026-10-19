namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The invariant -MAE &lt;= RExact &lt;= MFE over the oracle's series and the engine's seeded days, daily and swing series, with exactly one corner excluded by an explicit predicate (<see cref="IsQ3Corner"/>): an entry at the limit whose fill
/// minute is also the exit minute by EndOfData or MaxHold. There the favourable extreme of the fill minute is not counted (X5) while the exit is at the close, so MFE can be below R. That is an open owner decision (log entry Q3-MFE, class B);
/// when it is taken, delete the predicate (one line) and the property covers the corner too. The excluded count is printed and asserted above zero, so the exclusion cannot silently stop being the corner it names.
/// </summary>
public sealed class ExcursionInvariantTests(ITestOutputHelper output)
{
    private const double Slack = 1e-9;

    /// <summary>The one excluded corner.</summary>
    internal static bool IsQ3Corner(OracleTrade t) =>
        t.EntryFill == OracleEntryFill.AtLimit && t.ExitEpochSeconds == t.EntryEpochSeconds && t.ExitReason is OracleExitReason.EndOfData or OracleExitReason.MaxHold;

    private static (int Checked, int Excluded, List<string> Bad) Scan(IEnumerable<OracleResult> results, string label)
    {
        int checkedCount = 0, excluded = 0;
        var bad = new List<string>();
        foreach (var t in results.Select(r => r.Trade).OfType<OracleTrade>())
        {
            if (IsQ3Corner(t))
            {
                excluded++;
                continue;
            }

            checkedCount++;
            if (-t.MaeR > t.RExact + Slack || t.RExact > t.MfeR + Slack)
            {
                bad.Add($"{label}: {t.ExitReason} R {t.RExact} MAE {t.MaeR} MFE {t.MfeR}");
            }
        }

        return (checkedCount, excluded, bad);
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
    public void The_excursions_bracket_the_result_everywhere_but_the_named_corner()
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

        int total = 0, excludedTotal = 0;
        var bad = new List<string>();
        var planted = Scan(Planted(), "planted corner");
        output.WriteLine($"planted corner: {planted.Checked} checked, {planted.Excluded} excluded, {planted.Bad.Count} violations (the generators of all four families never reach the corner: 0 excluded above)");
        excludedTotal += planted.Excluded;
        bad.AddRange(planted.Bad);
        foreach (var (name, cases) in families)
        {
            var (n, ex, b) = Scan(Engine(cases), name);
            output.WriteLine($"{name}: {n} trades checked, {ex} excluded as the Q3 corner, {b.Count} violations");
            total += n;
            excludedTotal += ex;
            bad.AddRange(b);
        }

        bad.Take(10).ShouldBeEmpty();
        total.ShouldBeGreaterThan(1000);
        excludedTotal.ShouldBeGreaterThan(0, "the exclusion names a corner that the generators reach; if it is not reached any more the predicate is dead");
    }

    [Fact]
    public void The_sentinel_the_corner_does_violate_the_invariant_so_the_predicate_is_needed()
    {
        Tape t = new Tape().Day(Fx.Mon, "09:15", "10:00", 100.00m, [Fx.M("10:00", 100.30m, 100.60m, 100.05m, 100.20m)]);
        var c = Fx.Cand(t, OracleSide.Buy, Fx.Mon, "10:00", 100.00m, 99.00m);
        var r = OracleAdapter.Walk(OracleAdapter.Bars(t.ToArray()), [OracleAdapter.Candidate(c)], OracleAdapter.Rules(Fx.Intraday(OracleSlippage.None))).Results[0];

        IsQ3Corner(r.Trade!).ShouldBeTrue();
        r.Trade!.RExact.ShouldBeGreaterThan(r.Trade.MfeR);
    }
}
