namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Annex C of P3-08, the triage rehearsal for the costing (the cost half of criterion 11) and the chain control (criterion C4): a fault of the cost reference is left on in the oracle and the comparison of the author's generated cases is
/// run against the real engine; it must report disagreements, each of which the triage of D9 classes as C (an oracle bug: here a planted one). A comparison that cannot see a planted fault says nothing when it sees none. These are
/// "FaultTests" classes on purpose: they are the only places of the comparison that switch a cost fault on (the architecture test lets only classes with that suffix do so).
/// </summary>
public sealed class TriageRehearsalCostFaultTests(ITestOutputHelper output)
{
    private const int Seeds = 60;

    /// <summary>Every cost fault: the author's thirteen and the two added from the spec (STT sides swapped, the cost sign flipped).</summary>
    public static TheoryData<OracleCostFaults> Faults()
    {
        var d = new TheoryData<OracleCostFaults>();
        foreach (var f in Enum.GetValues<OracleCostFaults>().Where(f => f != OracleCostFaults.None))
        {
            d.Add(f);
        }

        return d;
    }

    /// <summary>The cases (of the given count in each of the 13 categories) on which the faulted oracle and the engine disagree, with the first few disagreements.</summary>
    private static (int Cases, int Disagreeing, List<string> First) Compare(OracleCostFaults fault, int seedsPerCategory)
    {
        var stats = new RatioStats();
        var cases = 0;
        var disagreeing = 0;
        var first = new List<string>();
        foreach (var category in Enum.GetValues<OracleCostCategory>())
        {
            for (long seed = 1; seed <= seedsPerCategory; seed++)
            {
                var source = OracleCostSeries.Of(category, seed);
                var engineSheet = CostAdapter.EngineSheet(source.SyntheticSheet);
                var oracle = CostView.Of(source.Run(fault), source.Input.Segment, engineSheet.Id);
                var engine = CostView.Of(CostAdapter.Apply(CostAdapter.EngineTrade(source.Input), engineSheet, CostAdapter.Profile(source.Sizing), source.Input.Capital, source.Input.MaxCostR, CostAdapter.Segment(source.Input.Segment)));
                var found = CostComparer.Diff(oracle, engine, stats, "rehearsal");
                cases++;
                if (found.Count > 0)
                {
                    disagreeing++;
                    if (first.Count < 3)
                    {
                        first.Add($"{category} seed {seed}: {found[0]}");
                    }
                }
            }
        }

        return (cases, disagreeing, first);
    }

    [Theory]
    [MemberData(nameof(Faults))]
    public void A_cost_fault_left_on_in_the_oracle_is_reported_as_disagreements_with_the_engine(OracleCostFaults fault)
    {
        var (cases, disagreeing, first) = Compare(fault, Seeds);

        output.WriteLine($"{fault}: {disagreeing} of {cases} cases disagree with the engine; class C (planted oracle fault); {string.Join(" | ", first)}");
        disagreeing.ShouldBeGreaterThan(0, $"the comparison did not see the planted fault {fault} on {cases} cases");
    }

    [Fact]
    public void The_sensitivity_of_the_comparison_to_each_fault_is_printed_and_kept_when_a_report_folder_is_named()
    {
        var lines = new List<string>();
        foreach (var fault in Enum.GetValues<OracleCostFaults>().Where(f => f != OracleCostFaults.None))
        {
            var (cases, disagreeing, first) = Compare(fault, Seeds);
            lines.Add($"{fault}: {disagreeing} of {cases} cases disagree with the engine; {string.Join(" | ", first)}");
            disagreeing.ShouldBeGreaterThan(0, fault.ToString());
        }

        Report.Write("cost-fault-sweep.txt", lines);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }
    }

    [Fact]
    public void With_no_fault_the_same_sample_has_no_disagreement()
    {
        var (cases, disagreeing, _) = Compare(OracleCostFaults.None, Seeds);

        cases.ShouldBe(13 * Seeds);
        disagreeing.ShouldBe(0);
    }

    [Fact]
    public void A_flipped_cost_sign_breaks_the_chain_on_at_least_10_trades_and_the_honest_oracle_breaks_it_on_none()
    {
        var honest = 0;
        var flipped = 0;
        var kept = 0;
        foreach (var category in new[] { OracleCostCategory.BuyTrade, OracleCostCategory.SellTrade, OracleCostCategory.IntradaySegment, OracleCostCategory.DeliverySegment })
        {
            for (long seed = 1; seed <= 150; seed++)
            {
                var source = OracleCostSeries.Of(category, seed);
                var good = source.Run();
                var bad = source.Run(OracleCostFaults.CostSignFlipped);
                if (good.Skip != OracleCostSkip.None)
                {
                    continue;
                }

                kept++;
                var engineSheet = CostAdapter.EngineSheet(source.SyntheticSheet);
                honest += CostTally.ChainHolds(source.Input, CostView.Of(good, source.Input.Segment, engineSheet.Id)) ? 0 : 1;
                flipped += CostTally.ChainHolds(source.Input, CostView.Of(bad, source.Input.Segment, engineSheet.Id)) ? 0 : 1;
            }
        }

        var line = $"kept {kept}: the chain is broken by the honest costing on {honest} trades and by the flipped sign on {flipped}";
        Report.Write("cost-chain-control.txt", [line]);
        output.WriteLine(line);
        kept.ShouldBeGreaterThan(100);
        honest.ShouldBe(0);
        flipped.ShouldBeGreaterThanOrEqualTo(10);
    }
}
