using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Remark;

/// <summary>P14-08 criteria 4 and 7 (series choice): the nearest series unless the planned last session reaches the last session before the expiry, the indices and shares each on the series the backfill holds, and an unknown expiry or a missing series counted by reason.</summary>
public sealed class SeriesPlannerTests
{
    private static readonly TradingCalendar Calendar = new();

    private static readonly MarketEpoch[] Epochs =
    [
        new(ExpiryRules.TuesdayEpoch, new DateOnly(2025, 9, 1), "NIFTY expiry on Tuesday", null, false),
        new(ExpiryRules.WeeklyEpoch, new DateOnly(2024, 11, 20), "one weekly index per exchange", null, false),
    ];

    private static SeriesPlanResult Plan(string symbol, DateOnly day, int hold = 1, bool sell = false, IEnumerable<ContractExpiry>? contracts = null)
    {
        var expiries = new ExpiryCalendar(symbol, contracts ?? [], Epochs, Calendar);
        return SeriesPlanner.Plan(symbol, day, SeriesPlanner.PlannedLastSession(Calendar, day, sell, hold), expiries, Epochs);
    }

    [Fact]
    public void An_intraday_trade_on_the_tuesday_before_a_thursday_expiry_uses_the_nearest_weekly()
    {
        var plan = Plan(ExpiryRules.Nifty, new DateOnly(2025, 6, 10)).Plan!;

        plan.Flag.ShouldBe('W');
        plan.Code.ShouldBe(1);
        plan.Expiry.Date.ShouldBe(new DateOnly(2025, 6, 12));
        plan.LastSession.ShouldBe(new DateOnly(2025, 6, 11));
        plan.IntervalMinutes.ShouldBe(5);
    }

    [Fact]
    public void A_trade_planned_to_reach_the_last_session_before_the_expiry_takes_the_next_series()
    {
        var plan = Plan(ExpiryRules.Nifty, new DateOnly(2025, 6, 10), hold: 2).Plan!;

        plan.Code.ShouldBe(2);
        plan.Expiry.Date.ShouldBe(new DateOnly(2025, 6, 19));
        Plan(ExpiryRules.Nifty, new DateOnly(2025, 6, 12)).Plan!.Code.ShouldBe(2);
    }

    [Fact]
    public void A_sell_is_always_intraday_so_its_hold_setting_does_not_move_the_series()
    {
        Plan(ExpiryRules.Nifty, new DateOnly(2025, 6, 10), hold: 5, sell: true).Plan!.Code.ShouldBe(1);
    }

    [Fact]
    public void Banknifty_uses_the_weekly_until_the_withdrawal_and_the_monthly_after()
    {
        var before = Plan(ExpiryRules.BankNifty, new DateOnly(2024, 9, 30)).Plan!;
        var after = Plan(ExpiryRules.BankNifty, new DateOnly(2024, 11, 21)).Plan!;

        before.Flag.ShouldBe('W');
        before.Expiry.Date.ShouldBe(new DateOnly(2024, 10, 2));
        after.Flag.ShouldBe('M');
        after.Expiry.Date.ShouldBe(new DateOnly(2024, 11, 28));
    }

    [Theory]
    [InlineData(2024, 11, 14)]
    [InlineData(2024, 11, 19)]
    [InlineData(2024, 11, 20)]
    public void Banknifty_trades_in_the_last_days_before_the_withdrawal_are_on_the_monthly_not_expiry_unknown(int year, int month, int day)
    {
        var plan = Plan(ExpiryRules.BankNifty, new DateOnly(year, month, day)).Plan!;

        plan.Flag.ShouldBe('M');
        plan.Expiry.Date.ShouldBe(new DateOnly(2024, 11, 28));
    }

    [Fact]
    public void Banknifty_weekly_has_no_second_series_so_a_hold_to_its_expiry_has_no_series_for_the_expiry()
    {
        Plan(ExpiryRules.BankNifty, new DateOnly(2024, 9, 30), hold: 3).Fate.ShouldBe(RemarkFates.NoSeriesForExpiry);
    }

    [Fact]
    public void A_share_is_on_the_monthly_at_fifteen_minutes_and_a_hold_to_its_expiry_has_no_series_for_the_expiry()
    {
        var plan = Plan("NSE:RELIANCE", new DateOnly(2025, 6, 10)).Plan!;

        plan.Flag.ShouldBe('M');
        plan.IntervalMinutes.ShouldBe(15);
        plan.Expiry.Date.ShouldBe(new DateOnly(2025, 6, 26));
        Plan("NSE:RELIANCE", new DateOnly(2025, 6, 25)).Fate.ShouldBe(RemarkFates.NoSeriesForExpiry);
    }

    [Fact]
    public void A_date_the_rule_cannot_give_is_expiry_unknown()
    {
        var expiries = new ExpiryCalendar(ExpiryRules.Nifty, [], [], Calendar);

        Plan("NSE:RELIANCE", new DateOnly(2030, 1, 1)).Plan.ShouldNotBeNull();
        SeriesPlanner.Plan(ExpiryRules.Nifty, new DateOnly(2025, 6, 10), new DateOnly(2025, 6, 10), expiries, []).Plan.ShouldNotBeNull();

        var none = new ExpiryCalendar("NSE:X", [], Epochs, new TradingCalendar([.. Enumerable.Range(0, 500).Select(i => TradingDay.Holiday(new DateOnly(2025, 6, 1).AddDays(i), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch))]));
        SeriesPlanner.Plan("NSE:X", new DateOnly(2025, 6, 10), new DateOnly(2025, 6, 10), none, Epochs).Fate.ShouldBe(RemarkFates.ExpiryUnknown);
    }

    [Fact]
    public void The_planned_last_session_counts_the_entry_session_as_the_first()
    {
        SeriesPlanner.PlannedLastSession(Calendar, new DateOnly(2025, 6, 13), false, 1).ShouldBe(new DateOnly(2025, 6, 13));
        SeriesPlanner.PlannedLastSession(Calendar, new DateOnly(2025, 6, 13), false, 3).ShouldBe(new DateOnly(2025, 6, 17));
        SeriesPlanner.PlannedLastSession(Calendar, new DateOnly(2025, 6, 13), true, 3).ShouldBe(new DateOnly(2025, 6, 13));
    }
}
