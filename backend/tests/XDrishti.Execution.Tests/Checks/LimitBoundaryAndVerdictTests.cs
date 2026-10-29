using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Tests.Checks;

public sealed class LimitBoundaryAndVerdictTests
{
    private static Facts With(Func<Facts, Facts> change) => change(Facts.Good());

    private static Facts Qty(Facts f, double qty) => f with { I = f.I with { Quantity = qty, Ticket = new TicketFacts(TicketState.Armed, "owner", false, qty) } };

    [Theory]
    [InlineData(66, true)] // 15 x 66 = 990 <= 1,000
    [InlineData(67, false)] // 1,005 > 1,000 although the profile allows 2,500
    public void Risk_per_trade_is_capped_by_the_inr_limit_at_the_boundary(double qty, bool pass) =>
        Qty(Facts.Good(), qty).Result(CheckCodes.RiskPerTrade).Outcome.ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(-4999, true)]
    [InlineData(-5000, false)] // limit = min(2 x 2,500, 1.5% x 500,000) = 5,000; reaching it fails
    [InlineData(1200, true)]
    public void Daily_loss_fails_at_the_limit(double pnl, bool pass) =>
        With(f => f with { A = f.A with { TodayPnl = new Stamped<double>(pnl, Facts.Now) } }).Result(CheckCodes.DailyLoss).Outcome.ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(1498, true)] // 0.13 %
    [InlineData(1540, false)] // 2.6 %
    [InlineData(1486, true)] // floor 1,485.10
    [InlineData(1485.05, false)]
    [InlineData(1485.10, true)]
    [InlineData(1530, false)] // at the target the trade is void
    public void The_collar_and_the_levels_the_price_has_passed(double ltp, bool pass) =>
        With(f => f with { M = f.M with { Quote = new Stamped<QuoteFacts>(new QuoteFacts(ltp, ltp - 0.1, ltp + 0.1, 1650, 1350, 100), Facts.Now) } }).Result(CheckCodes.LtpCollar).Outcome
            .ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(1649.90, 1600, 1649.50, true)] // upper - 2 ticks
    [InlineData(1649.95, 1600, 1649.50, false)]
    [InlineData(1350.10, 1350.05, 1400, true)] // lower + 2 ticks
    [InlineData(1350.05, 1350.05, 1400, false)]
    [InlineData(1500, 1350, 1530, false)] // a stop on the lower limit is not on the right side of it
    [InlineData(1500, 1485, 1650, false)] // a target on the upper limit cannot be reached
    public void The_circuit_band_keeps_two_ticks_clear_of_each_edge(double entry, double stop, double target, bool pass) =>
        With(f => f with { I = f.I with { Entry = entry, Stop = stop, Target = target } }).Result(CheckCodes.CircuitBand).Outcome.ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(1499.90, 1500.10, true)] // 0.20 <= max(0.15, 3.75)
    [InlineData(1498.00, 1502.00, false)] // 4.00
    [InlineData(1498.125, 1501.875, true)] // exactly 3.75
    public void The_spread_limit_is_the_larger_of_ticks_and_percent(double bid, double ask, bool pass) =>
        With(f => f with { M = f.M with { Quote = new Stamped<QuoteFacts>(new QuoteFacts(1500, bid, ask, 1650, 1350, 100), Facts.Now) } }).Result(CheckCodes.SpreadDepth).Outcome
            .ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(20, true)] // exactly 2 x quantity
    [InlineData(19, false)]
    public void Depth_must_hold_twice_the_quantity(double depth, bool pass) =>
        With(f => f with { M = f.M with { Quote = new Stamped<QuoteFacts>(new QuoteFacts(1500, 1499.9, 1500.1, 1650, 1350, depth), Facts.Now) } }).Result(CheckCodes.SpreadDepth).Outcome
            .ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(14_850, true)] // 14,850 + risk 150 = 15,000 = 3% of capital: at the limit passes
    [InlineData(14_850.01, false)]
    public void Exposure_at_the_limit_passes(double open, bool pass) =>
        With(f => f with { A = f.A with { IntentOpenRiskInr = open } }).Result(CheckCodes.ExposureHeadroom).Outcome.ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)] // reaching the limit fails
    public void Open_positions_fail_at_the_limit(int open, bool pass) =>
        With(f => f with { A = f.A with { OpenIntentEntries = open } }).Result(CheckCodes.OpenPositions).Outcome.ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Entries_today_must_leave_room_for_this_one(int sent, bool pass) =>
        With(f => f with { A = f.A with { EntriesToday = sent } }).Result(CheckCodes.EntriesToday).Outcome.ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    [Theory]
    [InlineData(2000, 50, true)] // quantity 2,000 x 50 = 100,000: notional at the cap passes
    [InlineData(2000, 50.05, false)]
    [InlineData(2001, 1, false)]
    [InlineData(2.5, 100, false)]
    public void Quantity_and_notional_caps(double qty, double entry, bool pass) =>
        With(f => Qty(f, qty) is var q ? q with { I = q.I with { Entry = entry } } : q).Result(CheckCodes.QuantityValid).Outcome.ShouldBe(pass ? CheckOutcome.Pass : CheckOutcome.Fail);

    // ---- verdicts and confirmations ------------------------------------------------------------------------------------------------------------------------

    private static readonly CheckConfirmation OwnerFor = new("owner:hem", "I accept", [CheckCodes.FnoBan], Facts.Now);

    private static Facts SoftBan() => With(f => f with { M = f.M with { InFnoBan = true } });

    [Fact]
    public void A_soft_failure_rejects_alone_and_is_cleared_only_by_an_exact_owner_confirmation_in_semi_auto()
    {
        var f = SoftBan();

        f.Run().Verdict.ShouldBe(CheckVerdict.Rejected);
        var cleared = f.Run(confirmation: OwnerFor);
        cleared.Verdict.ShouldBe(CheckVerdict.ClearedByConfirmation);
        cleared.AuthorisesSend.ShouldBeTrue();
        cleared.Confirmation.ShouldBe(OwnerFor);
    }

    [Fact]
    public void A_confirmation_is_void_when_the_soft_set_differs_in_any_way()
    {
        var f = SoftBan();
        var extra = f with { M = f.M with { UnderSurveillance = true } };
        var changedKind = f with { M = f.M with { InFnoBan = null } };

        extra.Run(confirmation: OwnerFor).Verdict.ShouldBe(CheckVerdict.Rejected);
        changedKind.Run(confirmation: OwnerFor).Verdict.ShouldBe(CheckVerdict.ClearedByConfirmation);
        (f with { M = f.M with { InFnoBan = false } }).Run(confirmation: OwnerFor).Verdict.ShouldBe(CheckVerdict.Cleared);
        f.Run(confirmation: OwnerFor with { Codes = [CheckCodes.FnoBan, CheckCodes.Surveillance] }).Verdict.ShouldBe(CheckVerdict.Rejected);
        f.Run(confirmation: OwnerFor with { Codes = [CheckCodes.Surveillance] }).Verdict.ShouldBe(CheckVerdict.Rejected);
    }

    [Fact]
    public void A_confirmation_from_an_auto_actor_or_without_a_reason_or_outside_semi_auto_is_worthless()
    {
        var f = SoftBan();
        f.Run(confirmation: OwnerFor with { Actor = "auto:rule" }).Verdict.ShouldBe(CheckVerdict.Rejected);
        f.Run(confirmation: OwnerFor with { Actor = "owner:" }).Verdict.ShouldBe(CheckVerdict.Rejected);
        f.Run(confirmation: OwnerFor with { Reason = " " }).Verdict.ShouldBe(CheckVerdict.Rejected);
        var auto = f with { S = f.S with { Mode = new ModeAnswer(ExecMode.AutoLive, true) } };
        auto.Run(confirmation: OwnerFor).Verdict.ShouldBe(CheckVerdict.Rejected);
    }

    [Fact]
    public void A_hard_failure_cannot_be_confirmed_and_a_confirmation_naming_a_hard_code_is_refused()
    {
        var f = With(x => x with { S = x.S with { LiveHalt = HaltReading.Halted }, M = x.M with { InFnoBan = true } });
        f.Run(confirmation: OwnerFor).Verdict.ShouldBe(CheckVerdict.Rejected);

        var softRun = SoftBan().Run();
        ExecChecklist.RefuseConfirmation(softRun, OwnerFor with { Codes = [CheckCodes.FnoBan, CheckCodes.NoHalt] }, ExecMode.SemiAuto).ShouldBe("hard_check_not_confirmable");
        ExecChecklist.RefuseConfirmation(f.Run(), OwnerFor, ExecMode.SemiAuto).ShouldBe("hard_check_not_confirmable");
    }

    [Fact]
    public void RefuseConfirmation_accepts_only_the_exact_set_from_the_owner_on_a_full_run_in_semi_auto()
    {
        var run = SoftBan().Run();

        ExecChecklist.RefuseConfirmation(run, OwnerFor, ExecMode.SemiAuto).ShouldBeNull();
        ExecChecklist.RefuseConfirmation(run, OwnerFor, ExecMode.AutoLive).ShouldBe("mode_not_semi_auto");
        ExecChecklist.RefuseConfirmation(run, OwnerFor, null).ShouldBe("mode_not_semi_auto");
        ExecChecklist.RefuseConfirmation(run, OwnerFor with { Actor = "auto:x" }, ExecMode.SemiAuto).ShouldBe("actor_or_reason_invalid");
        ExecChecklist.RefuseConfirmation(run, OwnerFor with { Codes = [CheckCodes.Surveillance] }, ExecMode.SemiAuto).ShouldBe("codes_do_not_match_run");
        ExecChecklist.RefuseConfirmation(SoftBan().Run(CheckRunKind.Advisory), OwnerFor, ExecMode.SemiAuto).ShouldBe("run_not_full");
        ExecChecklist.RefuseConfirmation(Facts.Good().Run(), OwnerFor, ExecMode.SemiAuto).ShouldBe("codes_do_not_match_run");
    }

    [Fact]
    public void Auto_live_with_one_soft_non_pass_is_rejected()
    {
        var f = SoftBan();
        (f with { S = f.S with { Mode = new ModeAnswer(ExecMode.AutoLive, true) } }).Run().Verdict.ShouldBe(CheckVerdict.Rejected);
    }
}
