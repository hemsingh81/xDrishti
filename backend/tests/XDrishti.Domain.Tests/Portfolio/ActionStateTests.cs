using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The owner's decision about an Action-center item (ADR 0022): repeating a decision is not a change; a snooze needs its end date.</summary>
public sealed class ActionStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly Until = new(2026, 10, 19);

    [Fact]
    public void A_snooze_keeps_its_end_date_and_a_done_has_none()
    {
        var snoozed = ActionState.Create("below_cost", "k", ActionDecision.Snoozed, Until, "owner@example.com", T0);
        var done = ActionState.Create("below_cost", "k", ActionDecision.Done, Until, "owner@example.com", T0);

        (snoozed.State, snoozed.SnoozedUntil).ShouldBe((ActionDecision.Snoozed, Until));
        (done.State, done.SnoozedUntil).ShouldBe((ActionDecision.Done, null));
    }

    [Fact]
    public void A_snooze_without_an_end_date_is_refused()
    {
        Should.Throw<ArgumentException>(() => ActionState.Create("below_cost", "k", ActionDecision.Snoozed, null, "owner@example.com", T0));
    }

    [Fact]
    public void Deciding_the_same_again_is_not_a_change_and_keeps_who_and_when()
    {
        var state = ActionState.Create("below_cost", "k", ActionDecision.Done, null, "first@example.com", T0);

        var changed = state.Decide(ActionDecision.Done, null, "second@example.com", T0.AddHours(1));

        (changed, state.UpdatedBy, state.UpdatedAt).ShouldBe((false, "first@example.com", T0));
    }

    [Fact]
    public void A_different_decision_or_a_later_snooze_date_is_a_change()
    {
        var state = ActionState.Create("below_cost", "k", ActionDecision.Snoozed, Until, "owner@example.com", T0);

        state.Decide(ActionDecision.Snoozed, Until.AddDays(2), "owner@example.com", T0.AddDays(2)).ShouldBeTrue();
        state.SnoozedUntil.ShouldBe(Until.AddDays(2));
        state.Decide(ActionDecision.Done, null, "owner@example.com", T0.AddDays(3)).ShouldBeTrue();
        state.SnoozedUntil.ShouldBeNull();
        state.Decide(ActionDecision.Cleared, null, "system:action-center", T0.AddDays(4)).ShouldBeTrue();
        (state.State, state.UpdatedBy, state.UpdatedAt).ShouldBe((ActionDecision.Cleared, "system:action-center", T0.AddDays(4)));
    }

    [Fact]
    public void The_rule_and_the_key_are_required_and_bounded()
    {
        Should.Throw<ArgumentException>(() => ActionState.Create("", "k", ActionDecision.Done, null, "o", T0));
        Should.Throw<ArgumentException>(() => ActionState.Create("below_cost", " ", ActionDecision.Done, null, "o", T0));
        Should.Throw<ArgumentException>(() => ActionState.Create("below_cost", new string('k', 101), ActionDecision.Done, null, "o", T0));
    }
}
