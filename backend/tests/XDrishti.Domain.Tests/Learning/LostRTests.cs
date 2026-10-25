using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>R lost per tag (P8-13) on a hand table, and that adding it changes no count.</summary>
public sealed class LostRTests
{
    private static readonly DateOnly Start = new(2026, 1, 5);

    private static LossTagInput Trade(int day, double exitPrice, double r, double mae, double mfe, ExitReason reason = ExitReason.Stop, int minutes = 60)
    {
        var t = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Start.AddDays(day), reason, entryMinute: 600, entry: 100, stop: 99, target: 102, exitPrice: exitPrice, rExact: r, rFilled: r);
        t = t with { MaeR = mae, MfeR = mfe, ExitEpochSeconds = t.EntryEpochSeconds + (minutes * 60) };
        return new LossTagInput("A", t, r);
    }

    [Fact]
    public void The_hand_table_gives_the_R_lost_of_each_tag_and_the_untagged_count()
    {
        var gap = Trade(0, 98.2, -1.8, 1.8, 0.1);
        var quick = Trade(1, 99, -1.0, 1.0, 0.1, minutes: 5);
        var untagged = Trade(2, 98.7, -1.3, 1.3, 0.3, ExitReason.SquareOff);

        var cell = LossTagAnalyzer.Analyze([gap, quick, untagged], new LossTagOptions()).Value.Cells.Single();

        cell.LostR.ShouldNotBeNull();
        cell.LostR[(int)LossTagKind.GapThroughStop].ShouldBe(-1.8, 1e-9);
        cell.LostR[(int)LossTagKind.QuickStop].ShouldBe(-1.0, 1e-9);
        cell.Untagged.ShouldBe(1);
        cell.Losses.ShouldBe(3);
    }

    [Fact]
    public void Counts_are_identical_with_and_without_the_R_lost_field()
    {
        var inputs = Enumerable.Range(0, 8).Select(i => Trade(i, 99, -1, 1, i % 2 == 0 ? 1.5 : 0.2)).ToList();

        var cell = LossTagAnalyzer.Analyze(inputs, new LossTagOptions()).Value.Cells.Single();
        var without = cell with { LostR = null };

        without.Counts.ShouldBe(cell.Counts);
        (without.Trades, without.Losses, without.Untagged).ShouldBe((cell.Trades, cell.Losses, cell.Untagged));
        cell.LostR!.Count.ShouldBe(cell.Counts.Count);
    }
}
