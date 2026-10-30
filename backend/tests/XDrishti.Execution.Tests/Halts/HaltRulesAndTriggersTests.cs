using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Tests.Halts;

/// <summary>P12-05 criteria 1, 5 and 11: the 25-cell permission table, the strongest-wins rule, and every trigger at, just inside and just beyond its boundary.</summary>
public sealed class HaltRulesAndTriggersTests
{
    // none, entries, cancel, exit_all, freeze x NewEntry, CancelWorkingEntry, PlaceOrRepairStop, TightenStop, Exit
    public static TheoryData<HaltLevel?, HaltAction, bool> Table()
    {
        var data = new TheoryData<HaltLevel?, HaltAction, bool>();
        HaltAction[] actions = [HaltAction.NewEntry, HaltAction.CancelWorkingEntry, HaltAction.PlaceOrRepairStop, HaltAction.TightenStop, HaltAction.Exit];
        (HaltLevel? Level, bool[] Row)[] rows =
        [
            (null, [true, true, true, true, true]),
            (HaltLevel.Entries, [false, true, true, true, true]),
            (HaltLevel.Cancel, [false, true, true, true, true]),
            (HaltLevel.ExitAll, [false, true, true, false, true]),
            (HaltLevel.Freeze, [false, false, true, false, true]),
        ];
        foreach (var (level, row) in rows)
        {
            for (var i = 0; i < actions.Length; i++)
            {
                data.Add(level, actions[i], row[i]);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void The_permission_table_has_exactly_the_25_cells_of_the_spec(HaltLevel? level, HaltAction action, bool permitted) =>
        HaltRules.Permits(level, action).ShouldBe(permitted);

    [Fact]
    public void Only_freeze_asks_for_a_fresh_broker_read_and_only_on_what_it_still_permits()
    {
        HaltRules.NeedsFreshBrokerRead(HaltLevel.Freeze, HaltAction.PlaceOrRepairStop).ShouldBeTrue();
        HaltRules.NeedsFreshBrokerRead(HaltLevel.Freeze, HaltAction.Exit).ShouldBeTrue();
        HaltRules.NeedsFreshBrokerRead(HaltLevel.Freeze, HaltAction.TightenStop).ShouldBeFalse("not permitted at all");
        HaltRules.NeedsFreshBrokerRead(HaltLevel.ExitAll, HaltAction.Exit).ShouldBeFalse();
        HaltRules.NeedsFreshBrokerRead(null, HaltAction.Exit).ShouldBeFalse();
    }

    [Fact]
    public void A_halt_never_adds_a_permission_each_level_permits_no_more_than_the_one_before_except_that_freeze_keeps_the_protective_two()
    {
        foreach (var action in Enum.GetValues<HaltAction>())
        {
            foreach (var level in Enum.GetValues<HaltLevel>())
            {
                if (HaltRules.Permits(level, action))
                {
                    HaltRules.Permits(null, action).ShouldBeTrue("no halt permits what no halt permits");
                }
            }
        }

        HaltRules.Permits((HaltLevel)99, HaltAction.Exit).ShouldBeFalse("an unknown level permits nothing");
    }

    [Fact]
    public void The_effective_state_is_the_strongest_halt_of_the_system_and_the_account_and_a_system_halt_covers_every_account()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        ActiveHalt Halt(long id, HaltScope scope, Guid? account, HaltLevel level) => new(id, DateTimeOffset.UnixEpoch, scope, account, level, "owner_button", "owner", "owner:x", "r");
        ActiveHalt[] halts = [Halt(1, HaltScope.Account, a, HaltLevel.Entries), Halt(2, HaltScope.System, null, HaltLevel.Cancel), Halt(3, HaltScope.Account, b, HaltLevel.ExitAll)];

        HaltRules.Effective(halts, a).ShouldBe(HaltLevel.Cancel);
        HaltRules.Effective(halts, b).ShouldBe(HaltLevel.ExitAll);
        HaltRules.Effective(halts, Guid.NewGuid()).ShouldBe(HaltLevel.Cancel, "the system halt covers an account with none of its own");
        HaltRules.Effective([Halt(4, HaltScope.Account, a, HaltLevel.Freeze), .. halts], a).ShouldBe(HaltLevel.Freeze);
        HaltRules.Effective([], a).ShouldBeNull();
    }

    [Fact]
    public void Every_trigger_code_of_the_spec_exists_once_and_only_the_named_ones_clear_themselves()
    {
        HaltTriggers.All.Count.ShouldBe(19, "the owner's button, the 17 automatic codes and the drill");
        HaltTriggers.All.Select(t => t.Code).Distinct().Count().ShouldBe(19);
        HaltTriggers.All.Where(t => t.AutoClear is not null).Select(t => t.Code).Order().ShouldBe(["broker_outage", "clock_skew", "drill", "feed_loss", "ip_not_allowed", "protection_unknown", "token_invalid"]);
        foreach (var owner in new[] { "owner_button", "daily_loss", "loss_cap", "drawdown", "disaster_loss", "reconciliation_break", "protection_breach", "flatten_incomplete", "oversold", "runaway_rate", "crash_loop", "exchange_halt" })
        {
            HaltTriggers.IsAutoClearing(owner).ShouldBeFalse(owner);
        }

        HaltTriggers.Find("reconciliation_break")!.DefaultLevel.ShouldBe(HaltLevel.Freeze);
        HaltTriggers.Find("runaway_rate")!.DefaultScope.ShouldBe(HaltScope.System);
    }

    [Fact]
    public void Daily_loss_with_capital_500000_and_budget_2500_has_the_limit_5000_and_fires_at_minus_5000_and_not_at_minus_4999()
    {
        var limit = Math.Min(2m * 2500m, 0.015m * 500000m);

        limit.ShouldBe(5000m);
        TriggerEvaluator.DailyLoss(-4999m, limit).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.DailyLoss(-5000m, limit).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.DailyLoss(-5001m, limit).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.DailyLoss(null, limit).ShouldBe(TriggerVerdict.Unavailable);
        TriggerEvaluator.DailyLoss(-9999m, null).ShouldBe(TriggerVerdict.Unavailable);
        TriggerEvaluator.DailyLoss(-9999m, 0m).ShouldBe(TriggerVerdict.Unavailable);
    }

    [Fact]
    public void Drawdown_from_a_peak_of_plus_20000_to_minus_20000_is_40000_which_is_8_percent_of_500000_and_fires_but_39999_does_not()
    {
        TriggerEvaluator.Drawdown(20000m, -20000m, 500000m, 8.0m).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.Drawdown(20000m, -19999m, 500000m, 8.0m).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.Drawdown(20000m, -20000m, null, 8.0m).ShouldBe(TriggerVerdict.Unavailable);
        TriggerEvaluator.Drawdown(null, 1m, 500000m, 8.0m).ShouldBe(TriggerVerdict.Unavailable);
        TriggerEvaluator.Drawdown(100m, 200m, 500000m, 8.0m).ShouldBe(TriggerVerdict.Unavailable, "a peak below the value now is a bad mark, not a pass");
    }

    [Fact]
    public void Disaster_loss_fires_at_3_percent_of_capital_and_not_just_inside()
    {
        TriggerEvaluator.DisasterLoss(-15000m, 500000m, 3.0m).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.DisasterLoss(-14999m, 500000m, 3.0m).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.DisasterLoss(-15000m, null, 3.0m).ShouldBe(TriggerVerdict.Unavailable);
    }

    [Fact]
    public void Feed_loss_needs_exposure_in_the_session_and_fires_at_the_limit()
    {
        TriggerEvaluator.FeedLoss(15, 15, exposure: true, inSession: true).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.FeedLoss(14.9, 15, exposure: true, inSession: true).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.FeedLoss(900, 15, exposure: false, inSession: true).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.FeedLoss(900, 15, exposure: true, inSession: false).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.FeedLoss(null, 15, exposure: true, inSession: true).ShouldBe(TriggerVerdict.Unavailable);
        TriggerEvaluator.FeedLoss(double.NaN, 15, exposure: true, inSession: true).ShouldBe(TriggerVerdict.Unavailable);
    }

    [Fact]
    public void Runaway_seven_sends_in_ten_seconds_fire_and_six_do_not_and_an_attempt_above_the_limiter_fires_at_once()
    {
        TriggerEvaluator.Runaway(7, 6, limiterExceeded: false).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.Runaway(6, 6, limiterExceeded: false).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.Runaway(0, 6, limiterExceeded: true).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.Runaway(null, 6, limiterExceeded: false).ShouldBe(TriggerVerdict.Unavailable);
        TriggerEvaluator.RejectBurst(5, 5).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.RejectBurst(4, 5).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.CrashLoop(3, 3).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.CrashLoop(2, 3).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.CrashLoop(null, 3).ShouldBe(TriggerVerdict.Unavailable);
    }

    [Fact]
    public void Clock_skew_needs_three_consecutive_reads_beyond_the_limit_in_either_direction()
    {
        TriggerEvaluator.ClockSkew([2500, -2600, 3000], 2000).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.ClockSkew([2500, 100, 3000], 2000).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.ClockSkew([9000, 9000], 2000).ShouldBe(TriggerVerdict.Unavailable);
        TriggerEvaluator.ClockSkew([2000, 2000, 2000], 2000).ShouldBe(TriggerVerdict.Clear, "at the limit is inside it");
    }

    [Fact]
    public void An_index_move_of_9_percent_either_way_is_an_exchange_halt_and_a_broker_outage_needs_exposure()
    {
        TriggerEvaluator.ExchangeHalt(20000m, 18200m, 9.0m).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.ExchangeHalt(20000m, 21800m, 9.0m).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.ExchangeHalt(20000m, 18201m, 9.0m).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.ExchangeHalt(null, 18200m, 9.0m).ShouldBe(TriggerVerdict.Unavailable);
        TriggerEvaluator.BrokerOutage(60, 60, exposure: true).ShouldBe(TriggerVerdict.Fire);
        TriggerEvaluator.BrokerOutage(59, 60, exposure: true).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.BrokerOutage(600, 60, exposure: false).ShouldBe(TriggerVerdict.Clear);
        TriggerEvaluator.BrokerOutage(null, 60, exposure: true).ShouldBe(TriggerVerdict.Unavailable);
    }

    [Fact]
    public void A_missing_or_old_day_mark_makes_the_drawdown_unavailable_and_a_good_one_is_judged_by_its_own_peak()
    {
        var account = Guid.NewGuid();
        var friday = new DateOnly(2026, 10, 23);
        var marks = new[] { new DayMark(account, friday, 0m, -20000m, 500000m, 20000m, DateTimeOffset.UnixEpoch) };

        DayMarks.Drawdown(marks, friday, 8.0m).ShouldBe(TriggerVerdict.Fire);
        DayMarks.Drawdown(marks, friday.AddDays(3), 8.0m).ShouldBe(TriggerVerdict.Unavailable, "the mark of the last session is missing");
        DayMarks.Drawdown([], friday, 8.0m).ShouldBe(TriggerVerdict.Unavailable);
        DayMarks.NextPeak(null, -50m).ShouldBe(0m);
        DayMarks.NextPeak(300m, 100m).ShouldBe(300m);
        DayMarks.NextPeak(300m, 450m).ShouldBe(450m);
    }
}
