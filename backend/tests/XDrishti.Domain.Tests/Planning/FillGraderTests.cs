using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>Spec P7-14: the fill grade, reproduced by hand (R and price slippage against the plan's levels).</summary>
public sealed class FillGraderTests
{
    private static readonly DateOnly Day = new(2026, 10, 20);
    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static JournalLink Link(JournalRole role, Side side, long qty, decimal price, decimal charges = 0m, long? used = null, int hour = 10) =>
        new(new JournalFill(Guid.NewGuid(), Account, "RELIANCE", side, Day, new TimeOnly(hour, 0), qty, price, charges), role, 1, Account, used ?? qty, "x");

    private static TicketGrade Plan(double entry = 100, double stop = 98, double r = 2) =>
        new(GradeStatus.Graded, r, 0.5, 2.5, "Target", entry, 104, "gross, no charges", "x", stop);

    [Fact]
    public void A_buy_is_graded_in_r_with_slippage_against_the_plan_by_hand()
    {
        // Plan: entry 100, stop 98 => 2 per share is 1R. Bought 10 at 100.5 (slippage 0.5 = 0.25R), sold 10 at 104; gross (104-100.5)*10 = 35, charges 3 => (35-3)/(2*10) = 1.6R.
        var entries = new[] { Link(JournalRole.Entry, Side.Buy, 10, 100.5m, 1.5m) };
        var exits = new[] { Link(JournalRole.Exit, Side.Sell, 10, 104m, 1.5m, hour: 13) };

        var grade = FillGrader.Grade(Side.Buy, entries, exits, Plan())!;

        grade.Status.ShouldBe(FillGrade.Graded);
        grade.GrossPnl.ShouldBe(35m);
        grade.Charges.ShouldBe(3m);
        grade.R.ShouldNotBeNull().ShouldBe(1.6, 1e-9);
        grade.SlippagePrice.ShouldNotBeNull().ShouldBe(0.5, 1e-9);
        grade.SlippageR.ShouldNotBeNull().ShouldBe(0.25, 1e-9);
        grade.PlanR.ShouldBe(2);
    }

    [Fact]
    public void A_sell_has_its_slippage_and_result_signed_by_the_side()
    {
        // Short: sold 10 at 99.5 against a plan entry of 100 (worse by 0.5 for a seller is a LOWER price: slippage +0.5), bought back at 97 => gross (99.5-97)*10 = 25; risk 2.
        var entries = new[] { Link(JournalRole.Entry, Side.Sell, 10, 99.5m) };
        var exits = new[] { Link(JournalRole.Exit, Side.Buy, 10, 97m, hour: 13) };

        var grade = FillGrader.Grade(Side.Sell, entries, exits, Plan(100, 102))!;

        grade.GrossPnl.ShouldBe(25m);
        grade.SlippagePrice.ShouldNotBeNull().ShouldBe(0.5, 1e-9);
        grade.R.ShouldNotBeNull().ShouldBe(1.25, 1e-9);
    }

    [Fact]
    public void Several_fills_are_volume_weighted_and_an_exit_uses_only_the_quantity_entered()
    {
        var entries = new[] { Link(JournalRole.Entry, Side.Buy, 4, 100m), Link(JournalRole.Entry, Side.Buy, 6, 101m, hour: 11) };
        var exits = new[] { Link(JournalRole.Exit, Side.Sell, 20, 103m, used: 10, hour: 13) };

        var grade = FillGrader.Grade(Side.Buy, entries, exits, Plan())!;

        grade.Qty.ShouldBe(10);
        grade.EntryPrice.ShouldBe(100.6, 1e-9);
        grade.GrossPnl.ShouldBe(24m); // (103 - 100.6) * 10
    }

    [Fact]
    public void An_open_position_is_not_graded()
    {
        var entries = new[] { Link(JournalRole.Entry, Side.Buy, 10, 100m) };
        var exits = new[] { Link(JournalRole.Exit, Side.Sell, 4, 101m, hour: 13) };

        FillGrader.Grade(Side.Buy, entries, exits, Plan()).ShouldBeNull();
        FillGrader.Grade(Side.Buy, entries, [], Plan()).ShouldBeNull();
    }

    [Theory]
    [InlineData(GradeStatus.NotTriggered)]
    [InlineData(GradeStatus.NotGradable)]
    public void A_plan_that_took_no_trade_leaves_the_round_trip_without_r(GradeStatus status)
    {
        var plan = new TicketGrade(status, null, null, null, null, null, null, "gross, no charges", "x");
        var entries = new[] { Link(JournalRole.Entry, Side.Buy, 10, 100m) };
        var exits = new[] { Link(JournalRole.Exit, Side.Sell, 10, 102m, hour: 13) };

        var grade = FillGrader.Grade(Side.Buy, entries, exits, plan)!;

        grade.Status.ShouldBe(FillGrade.NoPlanLevels);
        grade.R.ShouldBeNull();
        grade.SlippageR.ShouldBeNull();
        grade.GrossPnl.ShouldBe(20m);
    }

    [Fact]
    public void A_plan_grade_stored_before_the_stop_was_kept_has_no_r()
    {
        var old = new TicketGrade(GradeStatus.Graded, 1, 0.1, 1, "Target", 100, 102, "gross, no charges", "x");
        var entries = new[] { Link(JournalRole.Entry, Side.Buy, 1, 100m) };
        var exits = new[] { Link(JournalRole.Exit, Side.Sell, 1, 101m, hour: 13) };

        var grade = FillGrader.Grade(Side.Buy, entries, exits, old)!;

        grade.Status.ShouldBe(FillGrade.NoPlanLevels);
        grade.Detail.ShouldContain("no entry and stop");
    }

    [Fact]
    public void Grading_is_deterministic_and_labelled_as_fills()
    {
        var entries = new[] { Link(JournalRole.Entry, Side.Buy, 10, 100.5m) };
        var exits = new[] { Link(JournalRole.Exit, Side.Sell, 10, 104m, hour: 13) };

        FillGrader.Grade(Side.Buy, entries, exits, Plan()).ShouldBe(FillGrader.Grade(Side.Buy, entries, exits, Plan()));
        FillGrade.Basis.ShouldBe("realised fills, not simulated");
    }
}
