namespace XDrishti.Simulation.Tests.Oracle;

public sealed class FaultTests(ITestOutputHelper output)
{
    private static IReadOnlyList<OracleFaults> Faults() =>
        [.. Enum.GetValues<OracleFaults>().Where(f => f != OracleFaults.None)];

    public static TheoryData<OracleFaults> FaultData()
    {
        var d = new TheoryData<OracleFaults>();
        foreach (OracleFaults f in Faults())
        {
            d.Add(f);
        }

        return d;
    }

    private static bool Fails(HandRow row, bool exactMode, OracleFaults fault)
    {
        OracleResult res = ReferenceWalker.Walk(row.Bars, row.Candidate, RowCheck.RulesOf(row, exactMode), fault);
        return RowCheck.Compare(row.Expect, res, exactMode).Count > 0;
    }

    internal static List<string> RowsFailing(OracleFaults fault)
    {
        var failing = new List<string>();
        foreach (HandRow row in AllRows.Rows)
        {
            if (Fails(row, exactMode: false, fault) || Fails(row, exactMode: true, fault))
            {
                failing.Add(row.Id);
            }
        }

        return failing;
    }

    [Fact]
    public void There_are_at_least_fourteen_faults_and_the_enum_has_none_as_zero()
    {
        ((int)OracleFaults.None).ShouldBe(0);
        Faults().Count.ShouldBeGreaterThanOrEqualTo(14);
        Faults().Count.ShouldBe(16);
    }

    [Theory]
    [MemberData(nameof(FaultData))]
    public void Each_fault_makes_at_least_one_hand_row_fail(OracleFaults fault)
    {
        List<string> failing = RowsFailing(fault);
        output.WriteLine(fault + " -> " + string.Join(", ", failing));
        failing.ShouldNotBeEmpty("the fault " + fault + " is not caught by any hand row");
    }

    [Fact]
    public void Print_the_fault_to_rows_list()
    {
        var lines = new List<string>();
        foreach (OracleFaults f in Faults())
        {
            List<string> failing = RowsFailing(f);
            lines.Add($"{f} ({failing.Count}): {string.Join(", ", failing)}");
        }

        Report.Write("fault-to-rows.txt", lines);
        foreach (string l in lines)
        {
            output.WriteLine(l);
        }

        lines.Count.ShouldBe(16);
    }

    [Fact]
    public void No_fault_means_no_row_fails()
    {
        RowsFailing(OracleFaults.None).ShouldBeEmpty();
    }
}
