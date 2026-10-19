namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Acceptance criterion 11 of P3-08, the triage rehearsal, and the sensitivity of the whole pipeline: a fault of D8 is left on in the oracle and the comparison of the oracle's series is run against the real engine; it must report
/// disagreements, each of which the triage of D9 classes as C (an oracle bug: here a planted one). A comparison that cannot see a planted fault would say nothing when it sees none. This class is a "FaultTests" class on purpose: it is the
/// one place of the comparison that switches a fault on (the architecture test of D2 (d) lets only classes with that suffix do so).
/// </summary>
public sealed class TriageRehearsalFaultTests(ITestOutputHelper output)
{
    private const int RehearsalSeeds = 40;

    private const int SweepSeeds = 120;

    /// <summary>The faults of the rehearsal in the default check: entry, exit, order inside a minute, MaxHold and MAE faults (five of the sixteen; the sweep has all).</summary>
    public static TheoryData<OracleFaults> Rehearsed() => [OracleFaults.FillOnSignalMinute, OracleFaults.GapFilledAtStopPrice, OracleFaults.StopCheckedAfterTarget, OracleFaults.ExtraStopTicksOnMaxHold, OracleFaults.MaeFromFilledEntry];

    /// <summary>The series (of the given count in each of the 16 categories, in the modelled mode) on which the faulted oracle and the engine disagree, with the first few disagreements.</summary>
    private static (int Series, int Disagreeing, List<string> First) Compare(OracleFaults fault, int seedsPerCategory)
    {
        var stats = new RatioStats();
        var series = 0;
        var disagreeing = 0;
        var first = new List<string>();
        for (var category = 1; category <= OracleCategories.Count; category++)
        {
            for (long seed = 0; seed < seedsPerCategory; seed++)
            {
                var source = ComparisonCases.OfOracle(OracleSeries.Of(category, seed), exact: false);
                var group = source.Groups[0];
                var oracle = group.Oracle.Run(fault);
                var engine = OracleAdapter.Walk(source.Bars, group.EngineCandidates, group.EngineRules).Results;
                var found = new List<Mismatch>();
                for (var i = 0; i < engine.Count; i++)
                {
                    found.AddRange(OracleComparer.Diff(oracle.Results[i], engine[i], stats, "rehearsal"));
                }

                series++;
                if (found.Count > 0)
                {
                    disagreeing++;
                    if (first.Count < 3)
                    {
                        first.Add($"category {category} seed {seed}: {found[0]}");
                    }
                }
            }
        }

        return (series, disagreeing, first);
    }

    [Theory]
    [MemberData(nameof(Rehearsed))]
    public void A_fault_left_on_in_the_oracle_is_reported_as_disagreements_with_the_engine(OracleFaults fault)
    {
        var (series, disagreeing, first) = Compare(fault, RehearsalSeeds);

        output.WriteLine($"{fault}: {disagreeing} of {series} series disagree with the engine; class C (planted oracle fault); {string.Join(" | ", first)}");
        disagreeing.ShouldBeGreaterThan(0, $"the comparison did not see the planted fault {fault} on {series} series");
    }

    [Fact]
    public void With_no_fault_the_same_sample_has_no_disagreement()
    {
        var (series, disagreeing, _) = Compare(OracleFaults.None, RehearsalSeeds);

        series.ShouldBe(16 * RehearsalSeeds);
        disagreeing.ShouldBe(0);
    }

    /// <summary>The sweep (explicit): each of the 16 faults of D8 in turn, over 120 seeds of each category; the comparison must see every one, which shows how sensitive it is. The counts are printed.</summary>
    [Fact(Explicit = true)]
    public void Every_fault_of_the_walker_check_is_also_seen_by_the_seeded_comparison()
    {
        var lines = new List<string>();
        var unseen = new List<OracleFaults>();
        foreach (var fault in Enum.GetValues<OracleFaults>().Where(f => f != OracleFaults.None))
        {
            var (series, disagreeing, first) = Compare(fault, SweepSeeds);
            lines.Add($"{fault}: {disagreeing} of {series} series disagree with the engine; {string.Join(" | ", first)}");
            if (disagreeing == 0)
            {
                unseen.Add(fault);
            }
        }

        Report.Write("fault-sweep.txt", lines);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        unseen.ShouldBeEmpty();
    }
}
