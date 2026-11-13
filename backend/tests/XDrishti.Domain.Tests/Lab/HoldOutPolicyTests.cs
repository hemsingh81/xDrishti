using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-06, D4 and AC7, worked by hand: hold-out = max(6 months, 20 %) rounded up; folds = floor((development - 9) / 3).</summary>
public sealed class HoldOutPolicyTests
{
    private static readonly HoldOutOptions Options = new();
    private static readonly SelectionOptions Selection = new();

    [Fact]
    public void Twenty_five_point_two_months_keeps_6_leaves_19_point_2_and_3_folds()
    {
        var plan = HoldOutPolicy.FromMonths(25.2, Options, Selection);

        plan.LengthMonths.ShouldBe(6, "20 % is 5.04, under the 6-month floor");
        plan.DevelopmentMonths.ShouldBe(19.2);
        plan.Folds.ShouldBe(3, "tests at months 9-12, 12-15, 15-18");
        plan.Status.ShouldBe("ok");
    }

    [Fact]
    public void Sixty_months_keeps_12_leaves_48_and_13_folds()
    {
        var plan = HoldOutPolicy.FromMonths(60, Options, Selection);

        (plan.LengthMonths, plan.DevelopmentMonths, plan.Folds, plan.Status).ShouldBe((12, 48.0, 13, "ok"));
    }

    [Fact]
    public void Twelve_months_keeps_6_leaves_6_which_is_under_the_9_month_start_so_there_are_no_folds()
    {
        var plan = HoldOutPolicy.FromMonths(12, Options, Selection);

        (plan.LengthMonths, plan.DevelopmentMonths, plan.Folds, plan.Status).ShouldBe((6, 6.0, 0, "no_folds"));
    }

    [Fact]
    public void A_history_no_longer_than_the_hold_out_has_no_development_at_all()
    {
        HoldOutPolicy.FromMonths(5, Options, Selection).Status.ShouldBe("no_history");
        HoldOutPolicy.FromMonths(double.NaN, Options, Selection).Status.ShouldBe("no_history");
    }

    [Fact]
    public void A_share_that_is_not_a_whole_number_of_months_is_rounded_up()
    {
        HoldOutPolicy.FromMonths(33, Options, Selection).LengthMonths.ShouldBe(7, "20 % of 33 is 6.6");
    }

    [Fact]
    public void The_start_is_the_last_session_minus_the_length_plus_a_day_the_cut_of_the_ml_hold_out()
    {
        var plan = HoldOutPolicy.FromDates(new DateOnly(2024, 10, 1), new DateOnly(2026, 11, 3), Options, Selection);

        plan.Start.ShouldBe(new DateOnly(2026, 5, 4));
        plan.Start.ShouldBe(new DateOnly(2026, 11, 3).AddMonths(-6).AddDays(1));
        plan.Status.ShouldBe("ok");
    }

    [Fact]
    public void Dates_that_leave_nothing_before_the_start_are_no_history()
    {
        var plan = HoldOutPolicy.FromDates(new DateOnly(2026, 8, 1), new DateOnly(2026, 11, 3), Options, Selection);

        plan.Start.ShouldBeNull();
        plan.Status.ShouldBe("no_history");
    }

    [Fact]
    public void The_options_default_to_the_spec_proposals_with_the_auto_unlock_on_and_valid_and_range_checked()
    {
        var o = new HoldOutOptions();

        (o.Enforce, o.AutoUnlock, o.MinMonths, o.SharePercent, o.MinTrades, o.Retention).ShouldBe((false, true, 6, 20.0, 30, 0.5)); // AutoUnlock defaults ON (ADR 0282)
        o.Problems().ShouldBeEmpty();
        new HoldOutOptions(MinMonths: 0).Problems().ShouldNotBeEmpty();
        new HoldOutOptions(SharePercent: 4).Problems().ShouldNotBeEmpty();
        new HoldOutOptions(SharePercent: double.NaN).Problems().ShouldNotBeEmpty();
        new HoldOutOptions(MinTrades: 4).Problems().ShouldNotBeEmpty();
        new HoldOutOptions(Retention: 1.1).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void A_change_of_any_switch_or_value_changes_the_snapshot_hash()
    {
        var a = new HoldOutOptions().SnapshotHash();

        a.Length.ShouldBe(64);
        new HoldOutOptions(Enforce: true).SnapshotHash().ShouldNotBe(a);
        new HoldOutOptions(AutoUnlock: false).SnapshotHash().ShouldNotBe(a);
        new HoldOutOptions(AutoRelease: false).SnapshotHash().ShouldNotBe(a);
        new HoldOutOptions(BlockMonths: 3).SnapshotHash().ShouldNotBe(a);
        new HoldOutOptions(MinTrades: 31).SnapshotHash().ShouldNotBe(a);
        new RegistrationOptions(true).SnapshotHash().ShouldNotBe(new RegistrationOptions().SnapshotHash());
        new RegistrationOptions(false, true).SnapshotHash().ShouldNotBe(new RegistrationOptions().SnapshotHash());
    }
}
