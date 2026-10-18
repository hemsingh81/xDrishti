using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 25 of P3-03d: the library stays pure and the swing loop reuses the rules instead of copying them. The per-minute decision of X2 to X5 exists once (a scan of the sources of <c>Walking</c> finds one copy, and a negative
/// control proves the scan can see a second), a walk allocates the same number of bytes for a hold of 1 session and of 5, and the candidate gained no member (the seam of ADR 0037).
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class SwingStructureTests
{
    /// <summary>A line of the X2 rule that no other place may repeat: the stop is reached when the low of a buy (the high of a sell) is beyond it.</summary>
    private const string StopRuleMarker = "Beyond(isBuy, isBuy ? minute.Low : minute.High, stop)";

    private const string TargetRuleMarker = "Reaches(isBuy, isBuy ? minute.High : minute.Low, target)";

    private static string WalkingSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "XDrishti.Simulation", "Walking");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The sources of Walking were not found above " + AppContext.BaseDirectory);
    }

    private static int Occurrences(string text, string marker)
    {
        var count = 0;
        for (var at = text.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = text.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void The_stop_and_target_rows_X2_to_X5_are_written_once_in_the_sources_of_the_walking_stage()
    {
        var files = Directory.GetFiles(WalkingSourceDirectory(), "*.cs");
        var stop = files.ToDictionary(f => Path.GetFileName(f)!, f => Occurrences(File.ReadAllText(f), StopRuleMarker));
        var target = files.ToDictionary(f => Path.GetFileName(f)!, f => Occurrences(File.ReadAllText(f), TargetRuleMarker));

        stop.Values.Sum().ShouldBe(1, "the stop row exists once: " + string.Join(", ", stop.Where(p => p.Value > 0).Select(p => p.Key)));
        target.Values.Sum().ShouldBe(1, "the target row exists once");
        stop["MinuteRule.cs"].ShouldBe(1);
        target["MinuteRule.cs"].ShouldBe(1);
    }

    [Fact]
    public void The_scan_does_see_a_second_copy_a_swing_loop_that_wrote_the_rule_again_would_be_found()
    {
        var swingLoop = File.ReadAllText(Path.Combine(WalkingSourceDirectory(), "SwingWalk.cs"));
        var withACopy = swingLoop + "\nvar stopHit = " + StopRuleMarker + ";\n";

        Occurrences(swingLoop, StopRuleMarker).ShouldBe(0);
        Occurrences(withACopy, StopRuleMarker).ShouldBe(1);
    }

    [Fact]
    public void Both_loops_call_the_same_routine()
    {
        var directory = WalkingSourceDirectory();

        File.ReadAllText(Path.Combine(directory, "TradeWalker.cs")).ShouldContain("MinuteRule.Decide(");
        File.ReadAllText(Path.Combine(directory, "SwingWalk.cs")).ShouldContain("MinuteRule.Decide(");
    }

    [Fact]
    public void The_candidate_has_the_eleven_members_of_the_seam_and_no_exit_field()
    {
        typeof(EntryCandidate).GetConstructors().Single().GetParameters().Select(p => p.Name).ShouldBe(
            ["StrategyId", "Instrument", "Side", "DecisionBarIndex", "DecisionEpochSeconds", "TriggerTimeframe", "TriggerBar", "TriggerBarMinutes", "TriggerBarComplete", "StopLevel", "Trace"]);
    }

    private const int Walks = 200;

    private static (long Bytes, bool Disturbed) Measure(SwingHand hand)
    {
        var candidate = hand.Candidate();
        var rules = hand.Rules(FillRules.Default);
        Result<WalkOutcome>? sink = TradeWalker.Walk(candidate, hand.Bars, rules);
        var (allocated, disturbed, _) = AllocationProbe.Measure(window =>
        {
            Result<WalkOutcome>? last = null;
            window.Begin();
            for (var i = 0; i < Walks; i++)
            {
                last = TradeWalker.Walk(candidate, hand.Bars, rules);
            }

            window.End();
            last!.IsSuccess.ShouldBeTrue();
        });
        sink.IsSuccess.ShouldBeTrue();
        return (allocated, disturbed);
    }

    private static SwingHand Flat(int days, int sessions)
    {
        var hand = SwingHand.A("alloc" + days, sessions);
        var dates = new[] { Tue, Wed, Thu, Fri, NextMon };
        for (var i = 0; i < days - 1; i++)
        {
            hand.Flat(dates[i]);
        }

        return hand;
    }

    [Fact]
    public void A_walk_allocates_the_same_number_of_bytes_for_a_hold_of_one_session_and_of_five()
    {
        var one = Measure(Flat(1, 1));
        var five = Measure(Flat(5, 5));

        one.Bytes.ShouldBeGreaterThan(0, "the result objects are allocated");
        five.Bytes.ShouldBe(one.Bytes, one.Disturbed || five.Disturbed ? "a garbage collection disturbed a measurement" : "nothing is allocated per visited minute or per date");
    }
}
