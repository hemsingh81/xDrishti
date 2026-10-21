using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 12 of P4-02: the partial rules allocate nothing per minute beyond the legs of a trade. After one warm-up walk, a position that books T1 and lives 20 minutes allocates the same number of bytes as one that lives 330 minutes
/// (and, for the trail, reads 20 more bars of the tape), for <c>PARTIAL_BE</c>, <c>PARTIAL_TRAIL</c> and the time stop. The probe is the one of <see cref="WalkAllocationTests"/>.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class PartialWalkAllocationTests
{
    private const int Walks = 200;

    private static readonly EntryCandidate Candidate = Cand.Make(Side.Buy, 29, "09:45", 98.00, "09:30", (99.00, 100.50, 99.00, 100.50));

    private static Day BuyDay(string exit) => Day.Dense().Start(100.50).Set("09:45 100.50/100.50/100.50/100.50", "10:00 100.50/103.00/100.50/103.00", exit);

    private static ExitTape Tape()
    {
        var tape = new ExitTape(["atr"]);
        tape.Append(DayBuilder.Epoch(DayBuilder.Monday, "09:45"), 100.50, [double.NaN]);
        for (var minute = DayBuilder.MinuteOfDay("10:15"); minute <= DayBuilder.MinuteOfDay("15:30"); minute += 15)
        {
            tape.Append(DayBuilder.Epoch(DayBuilder.Monday, minute), 103.00, [0.5]);
        }

        return tape;
    }

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(2, 0.0)]
    [InlineData(0, 0.5)]
    public void A_walk_that_lives_20_minutes_allocates_as_much_as_one_that_lives_330_minutes(int partial, double minProgress)
    {
        var kind = (PartialKind)partial;
        var policy = new LegPolicy(kind, kind == PartialKind.None ? 0m : 0.5m, kind == PartialKind.Be ? 2.0m : 0m, kind == PartialKind.Trail ? 2.0m : 0m, kind == PartialKind.None ? (decimal)minProgress : null, kind == PartialKind.None ? 30 : 0, 0);
        var tape = Tape();
        var context = new ExitContext(null, tape, policy);
        var rules = new WalkRules(kind == PartialKind.None ? 2.0 : 1.0, 915, Cand.NoSlippage, Cand.Tick05);
        var shortExit = kind == PartialKind.Trail ? "10:20 103.00/103.00/101.50/101.50" : "10:20 103.00/103.00/100.50/100.50";
        var longExit = kind == PartialKind.Trail ? "15:00 103.00/103.00/101.50/101.50" : "15:00 103.00/103.00/100.50/100.50";
        var shortBars = kind == PartialKind.None ? Day.Dense().Start(100.50).Set("09:45 100.50/100.50/100.50/100.50", "10:20 100.50/100.50/97.50/97.50").Bars() : BuyDay(shortExit).Bars();
        var longBars = kind == PartialKind.None ? Day.Dense().Start(100.50).Set("09:45 100.50/100.50/100.50/100.50", "15:00 100.50/100.50/97.50/97.50").Bars() : BuyDay(longExit).Bars();
        Result<WalkOutcome>? sink = TradeWalker.Walk(Candidate, shortBars, rules, context);
        sink = TradeWalker.Walk(Candidate, longBars, rules, context);

        var (shortBytes, shortDisturbed) = Measure(shortBars, rules, context);
        var (longBytes, longDisturbed) = Measure(longBars, rules, context);

        sink.IsSuccess.ShouldBeTrue();
        shortBytes.ShouldBeGreaterThan(0, "the result objects and the legs are allocated");
        longBytes.ShouldBe(shortBytes, shortDisturbed || longDisturbed ? "a garbage collection disturbed a measurement" : "nothing is allocated per visited minute or per bar of the tape");
    }

    private static (long Allocated, bool Disturbed) Measure(IReadOnlyList<Bar> bars, WalkRules rules, ExitContext context)
    {
        var (allocated, disturbed, _) = AllocationProbe.Measure(window =>
        {
            Result<WalkOutcome>? last = null;
            window.Begin();
            for (var i = 0; i < Walks; i++)
            {
                last = TradeWalker.Walk(Candidate, bars, rules, context);
            }

            window.End();
            last!.IsSuccess.ShouldBeTrue();
        });
        return (allocated, disturbed);
    }
}
