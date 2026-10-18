using XDrishti.Domain.Common;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// AC11: a walk allocates its result objects and nothing per visited minute. After one warm-up walk, positions that live 11 minutes and 331 minutes allocate the same number of bytes. The probe is the third copy of the
/// one in the Indicators and Strategies tests (exact per-thread counter, protected from a collection on another thread).
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class WalkAllocationTests
{
    private const int Walks = 200;

    [Fact]
    public void A_walk_of_a_position_that_lives_16_minutes_allocates_as_much_as_one_that_lives_331_minutes()
    {
        AssertSameBytes(Cases.W("W5"), Cases.F("F5"), Cand.Standard);
    }

    [Fact]
    public void The_same_holds_with_the_doc_16_slippage_on_the_tick_grid()
    {
        AssertSameBytes(Cases.W("W5"), Cases.F("F5"), Cand.Modelled);
    }

    private static void AssertSameBytes(WalkCase shortLived, WalkCase longLived, WalkRules rules)
    {
        Result<WalkOutcome>? sink = null;
        sink = TradeWalker.Walk(shortLived.Items[0].Candidate, shortLived.Bars, rules);
        sink = TradeWalker.Walk(longLived.Items[0].Candidate, longLived.Bars, rules);

        var (shortBytes, shortDisturbed, _) = Measure(shortLived, rules);
        var (longBytes, longDisturbed, _) = Measure(longLived, rules);

        sink.IsSuccess.ShouldBeTrue();
        shortBytes.ShouldBeGreaterThan(0, "the result objects are allocated");
        longBytes.ShouldBe(shortBytes, shortDisturbed || longDisturbed ? "a garbage collection disturbed a measurement" : "nothing is allocated per visited minute");
    }

    private static (long Allocated, bool Disturbed, int Attempts) Measure(WalkCase walkCase, WalkRules rules)
    {
        var item = walkCase.Items[0];
        return AllocationProbe.Measure(window =>
        {
            Result<WalkOutcome>? last = null;
            window.Begin();
            for (var i = 0; i < Walks; i++)
            {
                last = TradeWalker.Walk(item.Candidate, walkCase.Bars, rules);
            }

            window.End();
            last!.IsSuccess.ShouldBeTrue();
        });
    }
}
