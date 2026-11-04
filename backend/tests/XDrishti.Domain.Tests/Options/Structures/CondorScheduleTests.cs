using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;

namespace XDrishti.Domain.Tests.Options.Structures;

/// <summary>P14-10 test plan: the condor schedule across a holiday and the 2025-09-01 weekday change, the regime gate per week, the weeklies BANKNIFTY no longer has, and the exit session before the expiry.</summary>
public sealed class CondorScheduleTests
{
    private static readonly MarketEpoch[] Epochs =
    [
        new(ExpiryRules.TuesdayEpoch, new DateOnly(2025, 9, 1), "NIFTY expiry on Tuesday", null, false),
        new(ExpiryRules.WeeklyEpoch, new DateOnly(2024, 11, 20), "one weekly index per exchange", null, false),
    ];

    private static TradingCalendar Holidays(params DateOnly[] dates) =>
        new(dates.Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)));

    private static CondorSchedule Build(string symbol, DateOnly from, DateOnly to, TradingCalendar? calendar = null, Func<DateOnly, bool>? range = null)
    {
        var cal = calendar ?? new TradingCalendar();
        return CondorSchedule.Build(symbol, from, to, cal, new ExpiryCalendar(symbol, [], Epochs, cal), Epochs, range ?? (_ => true));
    }

    [Fact]
    public void Before_the_tuesday_change_the_monday_entry_takes_the_thursday_three_sessions_away_and_leaves_at_the_close_of_the_wednesday()
    {
        var schedule = Build(ExpiryRules.Nifty, new DateOnly(2025, 6, 9), new DateOnly(2025, 6, 13));

        var entry = schedule.Entries.ShouldHaveSingleItem();
        entry.Date.ShouldBe(new DateOnly(2025, 6, 9));
        (entry.Plan.Flag, entry.Plan.Code, entry.Plan.Expiry.Date, entry.Plan.LastSession, entry.Plan.IntervalMinutes).ShouldBe(('W', 1, new DateOnly(2025, 6, 12), new DateOnly(2025, 6, 11), 5));
        (schedule.Weeks, schedule.NotRange).ShouldBe((1, 0));
    }

    [Fact]
    public void After_the_2025_09_01_change_the_monday_is_one_session_from_the_tuesday_so_the_entry_takes_the_following_weeks_expiry_as_series_code_2()
    {
        var schedule = Build(ExpiryRules.Nifty, new DateOnly(2025, 9, 8), new DateOnly(2025, 9, 12));

        var entry = schedule.Entries.ShouldHaveSingleItem();
        (entry.Plan.Code, entry.Plan.Expiry.Date, entry.Plan.LastSession).ShouldBe((2, new DateOnly(2025, 9, 16), new DateOnly(2025, 9, 15)));
    }

    [Fact]
    public void A_holiday_monday_moves_the_first_session_of_the_week_to_the_tuesday_which_is_still_two_sessions_from_the_thursday()
    {
        var schedule = Build(ExpiryRules.Nifty, new DateOnly(2025, 6, 9), new DateOnly(2025, 6, 13), Holidays(new DateOnly(2025, 6, 9)));

        var entry = schedule.Entries.ShouldHaveSingleItem();
        entry.Date.ShouldBe(new DateOnly(2025, 6, 10));
        (entry.Plan.Expiry.Date, entry.Plan.LastSession).ShouldBe((new DateOnly(2025, 6, 12), new DateOnly(2025, 6, 11)));
    }

    [Fact]
    public void A_holiday_thursday_moves_the_expiry_to_the_wednesday_which_is_one_session_from_the_tuesday_so_the_entry_takes_the_next_weeks_expiry()
    {
        var schedule = Build(ExpiryRules.Nifty, new DateOnly(2025, 6, 9), new DateOnly(2025, 6, 13), Holidays(new DateOnly(2025, 6, 9), new DateOnly(2025, 6, 12)));

        var entry = schedule.Entries.ShouldHaveSingleItem();
        entry.Date.ShouldBe(new DateOnly(2025, 6, 10));
        (entry.Plan.Code, entry.Plan.Expiry.Date, entry.Plan.LastSession).ShouldBe((2, new DateOnly(2025, 6, 19), new DateOnly(2025, 6, 18)));
    }

    [Fact]
    public void A_week_whose_previous_session_is_not_a_range_session_is_counted_and_not_entered()
    {
        var range = new HashSet<DateOnly> { new(2025, 6, 6) };

        var schedule = Build(ExpiryRules.Nifty, new DateOnly(2025, 6, 2), new DateOnly(2025, 6, 13), range: range.Contains);

        schedule.Entries.Select(e => e.Date).ShouldBe([new DateOnly(2025, 6, 9)]);
        (schedule.Weeks, schedule.NotRange).ShouldBe((2, 1));
    }

    [Fact]
    public void No_regime_label_at_all_enters_nothing_and_counts_every_week_as_not_range()
    {
        var schedule = Build(ExpiryRules.Nifty, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), range: _ => false);

        schedule.Entries.ShouldBeEmpty();
        schedule.NotRange.ShouldBe(schedule.Weeks);
        schedule.Weeks.ShouldBeGreaterThan(40);
    }

    [Fact]
    public void Banknifty_has_a_weekly_only_until_its_weeklies_were_withdrawn_and_then_every_week_is_counted_as_no_weekly()
    {
        var before = Build(ExpiryRules.BankNifty, new DateOnly(2024, 10, 7), new DateOnly(2024, 10, 11));
        var after = Build(ExpiryRules.BankNifty, new DateOnly(2025, 2, 3), new DateOnly(2025, 2, 7));

        before.Entries.Count.ShouldBe(1);
        after.Entries.ShouldBeEmpty();
        after.NoWeekly.ShouldBe(1);
    }

    [Fact]
    public void The_entry_epoch_is_0930_india_time_and_the_previous_session_skips_a_holiday()
    {
        CondorSchedule.EntryEpoch(new DateOnly(2025, 6, 9)).ShouldBe(new DateTimeOffset(2025, 6, 9, 9, 30, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds());
        CondorSchedule.PreviousSession(Holidays(new DateOnly(2025, 6, 6)), new DateOnly(2025, 6, 9)).ShouldBe(new DateOnly(2025, 6, 5));
    }
}
