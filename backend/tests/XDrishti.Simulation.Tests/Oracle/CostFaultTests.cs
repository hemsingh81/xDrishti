namespace XDrishti.Simulation.Tests.Oracle;

public sealed class CostFaultTests(ITestOutputHelper output)
{
    private static IReadOnlyList<OracleCostFaults> Faults() =>
        [.. Enum.GetValues<OracleCostFaults>().Where(f => f != OracleCostFaults.None)];

    public static TheoryData<OracleCostFaults> FaultData()
    {
        var d = new TheoryData<OracleCostFaults>();
        foreach (OracleCostFaults f in Faults())
        {
            d.Add(f);
        }

        return d;
    }

    internal static List<string> RowsFailing(OracleCostFaults fault) =>
        [.. CostExamples.All.Where(e => Differs(e, fault)).Select(e => e.Id)];

    private static bool Differs(CostExample e, OracleCostFaults fault)
    {
        try
        {
            return CostCheck.Compare(e, ReferenceCosting.Cost(e.Input, TestSheet.Sheet, e.Sizing, fault)).Count > 0;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    [Fact]
    public void A_sell_leg_charged_on_the_schedule_of_the_entry_date_gives_the_total_the_spec_names_for_L1()
    {
        // L1: the sell at 2026-03-04 is on schedule B; on the entry's schedule A the total would be 99.58, not 110.08.
        OracleCostInput t = CostExamples.All.Single(e => e.Id == "L1").Input;
        ReferenceCosting.Cost(t, TestSheet.Sheet, OracleSizing.Standard, OracleCostFaults.ExitLegUsesEntrySchedule).ExitLeg!.Total.ShouldBe(99.58m);
    }

    [Fact]
    public void The_faults_change_the_result_on_generated_cases_too()
    {
        foreach (OracleCostFaults f in Faults())
        {
            int differ = 0;
            foreach (OracleCostCategory c in Enum.GetValues<OracleCostCategory>())
            {
                for (int seed = 1; seed <= 100; seed++)
                {
                    OracleCostCase k = OracleCostSeries.Of(c, seed);
                    OracleCostResult a = k.Run();
                    OracleCostResult b = k.Run(f);
                    if (a.Skip != b.Skip || a.Quantity != b.Quantity || a.TotalCost != b.TotalCost || a.CostR != b.CostR || a.RNet != b.RNet)
                    {
                        differ++;
                    }
                }
            }

            output.WriteLine($"{f}: {differ} of {100 * Enum.GetValues<OracleCostCategory>().Length} generated cases differ");
            differ.ShouldBeGreaterThan(0, f.ToString());
        }
    }

    [Fact]
    public void There_are_at_least_eight_faults_none_is_zero_and_the_unfaulted_reference_passes_every_row()
    {
        ((int)OracleCostFaults.None).ShouldBe(0);
        Faults().Count.ShouldBeGreaterThanOrEqualTo(8);
        RowsFailing(OracleCostFaults.None).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(FaultData))]
    public void Each_fault_makes_at_least_one_worked_example_fail(OracleCostFaults fault)
    {
        List<string> rows = RowsFailing(fault);
        output.WriteLine($"{fault} -> {string.Join(", ", rows)}");
        rows.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_fault_to_rows_list()
    {
        var lines = Faults().Select(f => $"{f} -> {string.Join(", ", RowsFailing(f))}").ToList();

        Report.Write("cost-fault-rows.txt", lines);
        foreach (string line in lines)
        {
            output.WriteLine(line);
        }
    }
}
