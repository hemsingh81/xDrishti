using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-04 criterion 1 (scale), 3 (readiness) and the rules of the owner's step move.</summary>
public sealed class RampTests
{
    private static readonly Guid Account = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a01");

    [Theory]
    [InlineData(1, 0.25)]
    [InlineData(2, 0.5)]
    [InlineData(3, 1.0)]
    public void The_scale_is_the_factor_of_the_step(int step, double expected)
    {
        RampPolicy.Scale(new RampState(5, step, false), RampPolicy.DefaultFactors).ShouldBe(expected);
    }

    [Fact]
    public void No_step_means_no_scale_and_a_hold_does_not_change_it()
    {
        RampPolicy.Scale(null, RampPolicy.DefaultFactors).ShouldBeNull();
        RampPolicy.Scale(new RampState(5, 1, true), RampPolicy.DefaultFactors).ShouldBe(0.25);
        RampPolicy.Scale(new RampState(5, 1, false), [0.1, 0.5, 1.0]).ShouldBe(0.1);
    }

    [Theory]
    [InlineData(new[] { 0.25, 0.5, 1.0 }, true)]
    [InlineData(new[] { 0.1, 0.2, 0.3 }, true)]
    [InlineData(new[] { 0.3, 0.5, 1.0 }, false)]
    [InlineData(new[] { 0.25, 0.25, 1.0 }, false)]
    [InlineData(new[] { 0.25, 0.5 }, false)]
    [InlineData(new[] { 0.0, 0.5, 1.0 }, false)]
    [InlineData(new[] { 0.25, 0.5, 1.5 }, false)]
    public void The_factors_may_be_lower_never_higher_than_the_ceilings(double[] factors, bool valid)
    {
        RampPolicy.FactorsAreValid(factors).ShouldBe(valid);
        RampPolicy.Scale(new RampState(1, 1, false), factors).HasValue.ShouldBe(valid);
    }

    [Fact]
    public void The_first_step_is_s1_by_the_owner_with_a_reason()
    {
        var ok = RampEvent.OwnerSetStep(Account, null, 1, "owner:hem", " first step ");

        ok.IsSuccess.ShouldBeTrue();
        ok.Value.Kind.ShouldBe(RampEventKinds.SetStep);
        ok.Value.FromStep.ShouldBeNull();
        ok.Value.Reason.ShouldBe("first step");
        RampEvent.OwnerSetStep(Account, null, 2, "owner:hem", "x").Error.Code.ShouldBe(RampErrors.StepNotAdjacentCode);
        RampEvent.OwnerSetStep(Account, null, 1, "rule:ramp", "x").Error.Code.ShouldBe("live.owner_only");
        RampEvent.OwnerSetStep(Account, null, 1, "owner:hem", "  ").Error.Code.ShouldBe(ComplianceRecord.InvalidCode);
        RampEvent.OwnerSetStep(Account, null, 1, "owner:hem", new string('x', 301)).Error.Code.ShouldBe(ComplianceRecord.InvalidCode);
        RampEvent.OwnerSetStep(Account, null, 0, "owner:hem", "x").Error.Code.ShouldBe(ComplianceRecord.InvalidCode);
    }

    [Fact]
    public void A_raise_is_refused_until_the_gates_exist_and_a_skip_is_never_allowed()
    {
        RampEvent.OwnerSetStep(Account, new RampState(1, 1, false), 2, "owner:hem", "x").Error.Code.ShouldBe(RampErrors.GatesNotMetCode);
        RampEvent.OwnerSetStep(Account, new RampState(1, 1, false), 3, "owner:hem", "x").Error.Code.ShouldBe(RampErrors.StepNotAdjacentCode);
        RampEvent.OwnerSetStep(Account, new RampState(1, 1, false), 1, "owner:hem", "x").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_owner_can_lower_to_any_lower_step_and_the_hold_is_carried()
    {
        var down = RampEvent.OwnerSetStep(Account, new RampState(9, 3, true), 1, "owner:hem", "back to S1");

        down.IsSuccess.ShouldBeTrue();
        (down.Value.FromStep, down.Value.ToStep, down.Value.Hold).ShouldBe((3, 1, true));
    }

    private static LiveReadinessFacts Ready() => new(true, true, true, XDrishti.Domain.Accounts.ConnectionStatus.Connected, true, LiveServiceState.Enabled, LiveServiceState.Enabled, true, true, true);

    [Fact]
    public void Everything_in_place_has_no_failing_item()
    {
        var r = LiveReadiness.Evaluate(Ready());

        r.FailingHard.ShouldBeEmpty();
        r.FailingSoft.ShouldBeEmpty();
    }

    [Fact]
    public void Each_hard_item_fails_by_itself()
    {
        LiveReadiness.Evaluate(Ready() with { RoleOn = false }).FailingHard.Select(i => i.Code).ShouldBe([LiveReadiness.TradingRole]);
        LiveReadiness.Evaluate(Ready() with { SettingsComplete = false }).FailingHard.Select(i => i.Code).ShouldBe([LiveReadiness.TradingRole]);
        LiveReadiness.Evaluate(Ready() with { CapitalFixed = false }).FailingHard.Select(i => i.Code).ShouldBe([LiveReadiness.FixedCapital]);
        LiveReadiness.Evaluate(Ready() with { Connection = XDrishti.Domain.Accounts.ConnectionStatus.Expired }).FailingHard.Select(i => i.Code).ShouldBe([LiveReadiness.BrokerConnection]);
        LiveReadiness.Evaluate(Ready() with { StepSet = false }).FailingHard.Select(i => i.Code).ShouldBe([LiveReadiness.RampStep]);
        LiveReadiness.Evaluate(Ready() with { Capture = LiveServiceState.NotRegistered }).FailingHard.Single().Detail.ShouldContain("not registered");
        LiveReadiness.Evaluate(Ready() with { Settle = LiveServiceState.Paused }).FailingHard.Single().Detail.ShouldContain("paused");
    }

    [Fact]
    public void A_soft_item_that_could_not_be_read_is_never_a_pass()
    {
        var r = LiveReadiness.Evaluate(Ready() with { PaperPhaseMet = null, ChargeSheetConfirmed = false, OrderBookCaptureOn = false });

        r.FailingHard.ShouldBeEmpty();
        r.FailingSoft.Select(i => i.Code).ShouldBe([LiveReadiness.PaperPhase, LiveReadiness.ChargeSheet, LiveReadiness.OrderBook]);
    }
}
