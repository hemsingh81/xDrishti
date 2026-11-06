using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10d-1 (ADR 0220): the three ML switches, and which change needs a fresh password. Turning ml.influence.enabled or ml.auto_promote ON does; ml.auto_rollback (it lowers reliance on ML) and any OFF do not. Plus the part of the influence result that an automatic promotion reads.</summary>
public sealed class MlSwitchTests
{
    [Fact]
    public void The_three_names_are_the_spec_names()
    {
        MlSwitch.Names.ShouldBe(["ml.influence.enabled", "ml.auto_promote", "ml.auto_rollback"]);
        MlSwitch.IsKnown("ml.auto_rollback").ShouldBeTrue();
        MlSwitch.IsKnown("ml.other").ShouldBeFalse();
        MlSwitch.IsKnown(null).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(null, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, null, false)]
    [InlineData(null, false, false)]
    [InlineData(false, false, false)]
    [InlineData(null, null, false)]
    [InlineData(true, false, true)]
    public void A_fresh_password_is_needed_only_to_turn_influence_or_auto_promotion_on(bool? influence, bool? autoPromote, bool needed)
    {
        MlSwitch.NeedsFreshPassword(influence, autoPromote).ShouldBe(needed);
    }

    private static MlTermResult T(string name, MlTermState state) => new(name, state, state == MlTermState.Holds ? [] : [new MlInfluenceReason("x", "y")]);

    private static MlInfluenceResult Result(MlTermState holdOut = MlTermState.Holds, MlTermState forward = MlTermState.Holds, MlTermState alarm = MlTermState.Holds, MlTermState sw = MlTermState.Fails) =>
        new([T(MlInfluence.HoldOutBeatsNoMl, holdOut), T(MlInfluence.ForwardShadowOk, forward), T(MlInfluence.NoCalibrationAlarm, alarm), T(MlInfluence.SwitchOn, sw)]);

    [Fact]
    public void Without_the_switch_the_other_three_terms_must_hold_and_the_switch_is_ignored()
    {
        Result().HoldsWithoutSwitch.ShouldBeTrue();
        Result().MayInfluence.ShouldBeFalse("the switch is off: holding without it is never permission to influence");
        Result(sw: MlTermState.Holds).HoldsWithoutSwitch.ShouldBeTrue();
        Result(sw: MlTermState.Holds).MayInfluence.ShouldBeTrue();
    }

    [Theory]
    [InlineData(MlTermState.Fails)]
    [InlineData(MlTermState.Unavailable)]
    public void Any_other_term_that_does_not_hold_fails_it_and_an_unavailable_term_is_never_a_pass(MlTermState state)
    {
        Result(holdOut: state).HoldsWithoutSwitch.ShouldBeFalse();
        Result(forward: state).HoldsWithoutSwitch.ShouldBeFalse();
        Result(alarm: state).HoldsWithoutSwitch.ShouldBeFalse();
    }

    [Fact]
    public void A_result_that_is_not_the_four_terms_in_order_never_holds_without_the_switch()
    {
        new MlInfluenceResult([]).HoldsWithoutSwitch.ShouldBeFalse();
        new MlInfluenceResult([T(MlInfluence.HoldOutBeatsNoMl, MlTermState.Holds)]).HoldsWithoutSwitch.ShouldBeFalse();
        new MlInfluenceResult([T(MlInfluence.ForwardShadowOk, MlTermState.Holds), T(MlInfluence.HoldOutBeatsNoMl, MlTermState.Holds), T(MlInfluence.NoCalibrationAlarm, MlTermState.Holds), T(MlInfluence.SwitchOn, MlTermState.Holds)]).HoldsWithoutSwitch.ShouldBeFalse();
    }
}
