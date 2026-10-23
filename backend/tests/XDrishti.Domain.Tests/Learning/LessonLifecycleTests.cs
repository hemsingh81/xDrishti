using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The lesson state machine as a table (spec P6-16): every pair of states and the facts each legal move needs.</summary>
public sealed class LessonLifecycleTests
{
    private static readonly LessonFacts Owner = new(true, HasFix: true, FixJudged: true);

    public static TheoryData<LessonState, LessonState> AllPairs()
    {
        var data = new TheoryData<LessonState, LessonState>();
        foreach (var from in Enum.GetValues<LessonState>())
        {
            foreach (var to in Enum.GetValues<LessonState>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void The_table_has_exactly_the_four_moves()
    {
        LessonLifecycle.Moves.ShouldBe(
        [
            (LessonState.Open, LessonState.Linked),
            (LessonState.Open, LessonState.Dismissed),
            (LessonState.Linked, LessonState.Closed),
            (LessonState.Linked, LessonState.Dismissed),
        ]);
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_pair_outside_the_table_is_refused_naming_the_state_and_every_pair_in_it_is_allowed(LessonState from, LessonState to)
    {
        var refusals = LessonLifecycle.Refusals(from, to, Owner);

        if (LessonLifecycle.Moves.Contains((from, to)))
        {
            refusals.ShouldBeEmpty();
        }
        else
        {
            refusals.Count.ShouldBe(1);
            refusals[0].ShouldContain(from.Code());
        }
    }

    [Theory]
    [InlineData(LessonState.Closed)]
    [InlineData(LessonState.Dismissed)]
    public void A_final_state_cannot_move_and_says_it_is_final(LessonState final)
    {
        LessonLifecycle.Next(final).ShouldBeEmpty();
        LessonLifecycle.Refusals(final, LessonState.Open, Owner).Single().ShouldContain("final");
    }

    [Fact]
    public void An_open_lesson_cannot_be_closed_without_going_through_a_fix()
    {
        LessonLifecycle.Refusals(LessonState.Open, LessonState.Closed, Owner).Single().ShouldContain("linked");
    }

    [Theory]
    [InlineData(LessonState.Open, LessonState.Linked)]
    [InlineData(LessonState.Open, LessonState.Dismissed)]
    [InlineData(LessonState.Linked, LessonState.Closed)]
    [InlineData(LessonState.Linked, LessonState.Dismissed)]
    public void Every_move_is_the_owners(LessonState from, LessonState to)
    {
        LessonLifecycle.Refusals(from, to, Owner with { ByOwner = false }).ShouldContain(r => r.Contains("owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Closing_needs_a_linked_fix_with_a_recorded_experiment()
    {
        LessonLifecycle.Refusals(LessonState.Linked, LessonState.Closed, new LessonFacts(true, HasFix: true, FixJudged: false)).Single().ShouldContain("judge it first");
        LessonLifecycle.Refusals(LessonState.Linked, LessonState.Closed, new LessonFacts(true)).Single().ShouldContain("numbers");
    }

    [Fact]
    public void The_state_and_monitor_codes_read_back()
    {
        foreach (var s in Enum.GetValues<LessonState>())
        {
            LessonStateText.TryParse(s.Code()).ShouldBe(s);
        }

        foreach (var k in Enum.GetValues<LessonMonitorKind>())
        {
            LessonMonitors.TryParse(k.Code()).ShouldBe(k);
        }

        LessonStateText.TryParse("nope").ShouldBeNull();
        LessonMonitors.TryParse("execution_gap").ShouldBeNull();
        LessonMonitors.NotBuilt.ShouldContain(n => n.StartsWith("execution_gap", StringComparison.Ordinal));
        LessonMonitors.NotBuilt.ShouldContain(n => n.StartsWith("decision_drag", StringComparison.Ordinal));
    }
}
