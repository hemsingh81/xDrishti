using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 10 of P4-04: a pyramid walk allocates the trade, its legs and nothing per visited minute. After one warm-up walk, a position that makes one add and is stopped out five minutes later and one that makes the same add and lives
/// 330 minutes reading 22 bars of the tape allocate the same number of bytes. The probe is the one of <see cref="WalkAllocationTests"/>. The throughput of the <c>FIXED_RR</c> path against its P4-01 figure (the 5 % of criterion 10) is an
/// explicit benchmark of <see cref="TrailAllocationTests"/>, run by hand in Release on a quiet machine.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class PyramidAllocationTests
{
    private const int Walks = 200;

    private static PyramidRow Row(bool shortLived) => new(
        shortLived ? "short" : "long",
        Side.Buy,
        shortLived ? ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25", "10:05 101.25/101.25/98.50/98.60"] : ["09:59 100.00/101.20/100.00/101.20", "10:00 101.25/101.25/101.25/101.25"],
        [.. Enumerable.Range(0, 22).Select(i => new TrailBar(Day.Clock(600 + (15 * i)), 101.20, double.NaN))],
        new PyramidWant(ExitReason.Stop, "10:05", 99.21875, 0, 0, 0, []));

    [Fact]
    public void A_position_with_an_add_stopped_five_minutes_later_allocates_as_much_as_one_that_lives_330_minutes()
    {
        _ = PyramidHand.Walk(Row(true));
        _ = PyramidHand.Walk(Row(false));

        var (shortBytes, shortDisturbed) = Measure(Row(true));
        var (longBytes, longDisturbed) = Measure(Row(false));

        shortBytes.ShouldBeGreaterThan(0, "the result objects and the legs are allocated");
        longBytes.ShouldBe(shortBytes, shortDisturbed || longDisturbed ? "a garbage collection disturbed a measurement" : "nothing is allocated per visited minute or per bar read");
    }

    private static (long Allocated, bool Disturbed) Measure(PyramidRow row)
    {
        var (candidate, bars, rules, context) = PyramidHand.Prepare(row);
        var (allocated, disturbed, _) = AllocationProbe.Measure(window =>
        {
            Result<WalkOutcome>? last = null;
            window.Begin();
            for (var i = 0; i < Walks; i++)
            {
                last = TradeWalker.Walk(candidate, bars, rules, context);
            }

            window.End();
            last!.IsSuccess.ShouldBeTrue();
            last.Value.Trade!.Legs!.Count.ShouldBe(3, "one add in both walks");
        });
        return (allocated, disturbed);
    }
}
