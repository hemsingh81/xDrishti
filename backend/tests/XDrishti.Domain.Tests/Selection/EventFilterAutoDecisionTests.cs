using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>
/// P15-06 criterion 9 (CLAUDE.md "approvals have an auto option"): the pure auto rule. Only a KEEP verdict with its evidence reference, for the current rule version, with approval auto in force, turns the filter on; an automatically enabled filter turns off when the newest verdict is an
/// explicit non-KEEP verdict; an owner-enabled filter is never turned off by it; unavailable never counts and never turns anything off.
/// </summary>
public sealed class EventFilterAutoDecisionTests
{
    private const string Current = "evf-1";

    private static EventFilterVerdict Verdict(string state, string version = Current, string? reference = "pf-2026-11-02") => new(state, version, reference);

    private static EventFilterState State(bool enabled, string approval = EventFilterState.AutoApproval, string? by = EventFilterState.OwnerDecision, int days = 2) => new(enabled, days, approval, by);

    private static (EventFilterAutoAction, string) Decide(EventFilterVerdict? verdict, EventFilterState state)
    {
        var d = EventFilterAutoDecision.Decide(verdict, state, Current);
        return (d.Action, d.Reason);
    }

    [Fact]
    public void A_keep_verdict_with_evidence_turns_an_off_filter_on_when_approval_is_auto()
    {
        var d = EventFilterAutoDecision.Decide(Verdict("KEEP"), State(false), Current);

        (d.Action, d.Reason, d.VerdictReference).ShouldBe((EventFilterAutoAction.TurnOn, "keep", "pf-2026-11-02"));
    }

    [Fact]
    public void Approval_manual_means_nothing_happens_whatever_the_verdict_says()
    {
        Decide(Verdict("KEEP"), State(false, EventFilterState.ManualApproval)).ShouldBe((EventFilterAutoAction.None, "approval_manual"));
        Decide(Verdict("DROP"), State(true, EventFilterState.ManualApproval, EventFilterState.AutoDecision)).ShouldBe((EventFilterAutoAction.None, "approval_manual"));
    }

    [Theory]
    [InlineData("DROP")]
    [InlineData("INCONCLUSIVE")]
    [InlineData("NOT_EVALUABLE")]
    public void Any_state_but_keep_does_not_turn_it_on(string state)
    {
        Decide(Verdict(state), State(false)).ShouldBe((EventFilterAutoAction.None, "already_off"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unavailable")]
    [InlineData("keep")]
    [InlineData("MAYBE")]
    public void An_unavailable_or_unknown_verdict_never_counts_as_keep(string? state)
    {
        EventFilterVerdict? verdict = state is null ? null : new EventFilterVerdict(state, Current, "ref");

        Decide(verdict, State(false)).Item1.ShouldBe(EventFilterAutoAction.None);
        Decide(verdict, State(true, by: EventFilterState.AutoDecision)).Item1.ShouldBe(EventFilterAutoAction.None, "an unreadable verdict does not flip a filter");
    }

    [Fact]
    public void A_keep_without_an_evidence_reference_or_for_another_rule_version_is_not_applied()
    {
        Decide(Verdict("KEEP", reference: null), State(false)).ShouldBe((EventFilterAutoAction.None, "keep_without_evidence"));
        Decide(Verdict("KEEP", reference: "  "), State(false)).ShouldBe((EventFilterAutoAction.None, "keep_without_evidence"));
        Decide(Verdict("KEEP", "evf-0"), State(false)).ShouldBe((EventFilterAutoAction.None, "verdict_stale"));
        Decide(Verdict("DROP", "evf-0"), State(true, by: EventFilterState.AutoDecision)).ShouldBe((EventFilterAutoAction.None, "verdict_stale"));
    }

    [Fact]
    public void A_filter_the_auto_rule_turned_on_turns_itself_off_when_the_verdict_stops_being_keep()
    {
        var auto = State(true, by: EventFilterState.AutoDecision);

        Decide(Verdict("DROP"), auto).ShouldBe((EventFilterAutoAction.TurnOff, "not_keep"));
        Decide(Verdict("INCONCLUSIVE"), auto).ShouldBe((EventFilterAutoAction.TurnOff, "not_keep"));
        Decide(Verdict("NOT_EVALUABLE"), auto).ShouldBe((EventFilterAutoAction.TurnOff, "not_keep"));
        Decide(Verdict("KEEP"), auto).ShouldBe((EventFilterAutoAction.None, "already_on"));
    }

    [Theory]
    [InlineData("DROP")]
    [InlineData("INCONCLUSIVE")]
    [InlineData("NOT_EVALUABLE")]
    public void A_filter_the_owner_turned_on_is_never_turned_off_by_the_auto_rule(string state)
    {
        Decide(Verdict(state), State(true, by: EventFilterState.OwnerDecision)).ShouldBe((EventFilterAutoAction.None, "owner_enabled"));
    }

    [Fact]
    public void A_filter_the_owner_switched_off_is_not_turned_on_again_by_a_keep_verdict_until_the_owner_acts()
    {
        var ownerOff = new EventFilterState(false, 2, "auto", "owner", OwnerSwitchedOff: true);

        Decide(Verdict("KEEP"), ownerOff).ShouldBe((EventFilterAutoAction.None, "owner_off"));
        Decide(Verdict("KEEP"), State(false)).Item1.ShouldBe(EventFilterAutoAction.TurnOn, "an owner row that only arms auto while the filter was already off is not an owner OFF");
        Decide(Verdict("KEEP"), ownerOff with { OwnerSwitchedOff = false }).Item1.ShouldBe(EventFilterAutoAction.TurnOn);
    }

    [Fact]
    public void A_turn_off_needs_the_reference_of_the_verdict_that_decided()
    {
        Decide(Verdict("DROP", reference: null), State(true, by: EventFilterState.AutoDecision)).ShouldBe((EventFilterAutoAction.None, "verdict_unavailable"));
    }

    [Fact]
    public void The_rule_has_no_way_to_change_the_window_or_the_approval()
    {
        typeof(EventFilterAutoDecisionResult).GetProperties().Select(p => p.Name).ShouldBe(["Action", "Reason", "VerdictReference"]);
        Should.Throw<ArgumentException>(() => EventFilterAutoDecision.Decide(null, State(false), " "));
    }

    [Fact]
    public void The_defaults_are_off_two_sessions_and_manual()
    {
        EventFilterState.Off.ShouldBe(new EventFilterState(false, 2, "manual", null));
        EventFilterState.IsApproval("auto").ShouldBeTrue();
        EventFilterState.IsApproval("Auto").ShouldBeFalse();
        EventFilterState.AutoActor.ShouldBe("auto:event-filter");
    }
}
