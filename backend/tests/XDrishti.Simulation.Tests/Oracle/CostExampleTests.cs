namespace XDrishti.Simulation.Tests.Oracle;

public sealed class CostExampleTests
{
    public static TheoryData<string> Ids()
    {
        var d = new TheoryData<string>();
        foreach (CostExample e in CostExamples.All)
        {
            d.Add(e.Id);
        }

        return d;
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_reference_equals_every_printed_line_of_the_example(string id)
    {
        CostExample e = CostExamples.All.Single(x => x.Id == id);
        CostCheck.Mismatches(e).ShouldBeEmpty();
    }

    [Fact]
    public void The_id_table_names_every_worked_example_and_each_encoded_id_exists()
    {
        string[] spec = ["W1", "W1b", "W2", "W3", "W4", "W5", "W6", "W7", "W8", "L1"];
        CostExamples.IdTable.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(spec.OrderBy(k => k, StringComparer.Ordinal));
        var ids = CostExamples.All.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string encoded in CostExamples.IdTable.Values.SelectMany(v => v))
        {
            ids.ShouldContain(encoded);
        }

        ids.Count.ShouldBe(CostExamples.IdTable.Values.Sum(v => v.Length));
    }

    [Fact]
    public void The_spec_sums_by_group_hold_for_W1_and_W2()
    {
        OracleCostResult w1 = ReferenceCosting.Cost(CostExamples.All.Single(e => e.Id == "W1").Input, TestSheet.Sheet, OracleSizing.Standard);
        (w1.EntryLeg!.Brokerage + w1.ExitLeg!.Brokerage).ShouldBe(40.00m);
        (w1.EntryLeg.Exchange + w1.ExitLeg.Exchange).ShouldBe(25.26m);
        (w1.EntryLeg.Sebi + w1.ExitLeg.Sebi).ShouldBe(2.53m);
        (w1.EntryLeg.Gst + w1.ExitLeg.Gst).ShouldBe(12.21m);
        OracleCostResult w2 = ReferenceCosting.Cost(CostExamples.All.Single(e => e.Id == "W2").Input, TestSheet.Sheet, OracleSizing.Standard);
        (w2.EntryLeg!.Exchange + w2.ExitLeg!.Exchange).ShouldBe(24.72m);
        (w2.EntryLeg.Sebi + w2.ExitLeg.Sebi).ShouldBe(2.47m);
        (w2.EntryLeg.Gst + w2.ExitLeg.Gst).ShouldBe(12.10m);
    }
}
