using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Remark;

/// <summary>P14-08 criterion 4 (expiry dates): the derived weekday rule across the 2025-09-01 and 2024-11-20 epochs, a holiday moving the date to the previous trading day, a contract date winning over the derived one, and an unknown expiry being null.</summary>
public sealed class ExpiryCalendarTests
{
    private static readonly MarketEpoch[] Epochs =
    [
        new(ExpiryRules.TuesdayEpoch, new DateOnly(2025, 9, 1), "NIFTY expiry on Tuesday", null, false),
        new(ExpiryRules.WeeklyEpoch, new DateOnly(2024, 11, 20), "one weekly index per exchange", null, false),
    ];

    private static TradingCalendar Holidays(params DateOnly[] dates) =>
        new(dates.Select(d => TradingDay.Holiday(d, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)));

    private static ExpiryCalendar Of(string symbol = ExpiryRules.Nifty, TradingCalendar? calendar = null, IEnumerable<ContractExpiry>? contracts = null, IEnumerable<MarketEpoch>? epochs = null) =>
        new(symbol, contracts ?? [], epochs ?? Epochs, calendar ?? new TradingCalendar());

    [Fact]
    public void Nifty_weeklies_are_thursdays_until_2025_08_31_and_tuesdays_from_2025_09_01()
    {
        var calendar = Of();

        calendar.Nth('W', new DateOnly(2025, 8, 4), 1)!.Date.ShouldBe(new DateOnly(2025, 8, 7));
        calendar.Nth('W', new DateOnly(2025, 8, 4), 2)!.Date.ShouldBe(new DateOnly(2025, 8, 14));
        calendar.Nth('W', new DateOnly(2025, 9, 1), 1)!.Date.ShouldBe(new DateOnly(2025, 9, 2));
        calendar.Nth('W', new DateOnly(2025, 8, 28), 1)!.Date.ShouldBe(new DateOnly(2025, 8, 28));
        calendar.Nth('W', new DateOnly(2025, 8, 28), 2)!.Date.ShouldBe(new DateOnly(2025, 9, 2));
        calendar.Nth('W', new DateOnly(2025, 8, 4), 1)!.FromContract.ShouldBeFalse();
        calendar.Nth('W', new DateOnly(2025, 8, 4), 1)!.Label.ShouldBe("expiry: derived");
    }

    [Fact]
    public void Monthlies_are_the_last_thursday_then_the_last_tuesday()
    {
        var calendar = Of();

        calendar.Nth('M', new DateOnly(2025, 8, 1), 1)!.Date.ShouldBe(new DateOnly(2025, 8, 28));
        calendar.Nth('M', new DateOnly(2025, 8, 29), 1)!.Date.ShouldBe(new DateOnly(2025, 9, 30));
        calendar.Nth('M', new DateOnly(2025, 8, 1), 2)!.Date.ShouldBe(new DateOnly(2025, 9, 30));
    }

    [Fact]
    public void A_thursday_expiry_on_a_holiday_moves_to_the_wednesday()
    {
        var calendar = Of(calendar: Holidays(new DateOnly(2025, 6, 12)));

        calendar.Nth('W', new DateOnly(2025, 6, 10), 1)!.Date.ShouldBe(new DateOnly(2025, 6, 11));
        calendar.LastSessionBefore(new DateOnly(2025, 6, 11)).ShouldBe(new DateOnly(2025, 6, 10));
    }

    [Fact]
    public void A_contract_date_wins_over_the_derived_one_and_is_labelled_so()
    {
        var calendar = Of(contracts: [new ContractExpiry(new DateOnly(2025, 6, 11), 'W')]);

        var expiry = calendar.Nth('W', new DateOnly(2025, 6, 10), 1)!;

        expiry.Date.ShouldBe(new DateOnly(2025, 6, 11));
        expiry.FromContract.ShouldBeTrue();
        expiry.Label.ShouldBe("expiry: contract");
        calendar.Nth('W', new DateOnly(2025, 6, 10), 2)!.Date.ShouldBe(new DateOnly(2025, 6, 19));
    }

    [Fact]
    public void Banknifty_weeklies_are_wednesdays_in_their_last_era_and_none_after_the_withdrawal()
    {
        var calendar = Of(ExpiryRules.BankNifty);

        calendar.Nth('W', new DateOnly(2024, 10, 1), 1)!.Date.ShouldBe(new DateOnly(2024, 10, 2));
        calendar.Nth('W', new DateOnly(2024, 11, 21), 1).ShouldBeNull();
        calendar.Nth('M', new DateOnly(2024, 10, 1), 1)!.Date.ShouldBe(new DateOnly(2024, 10, 31));
    }

    [Fact]
    public void A_share_has_no_weekly_and_a_date_the_rule_cannot_give_is_null_never_a_guess()
    {
        Of("NSE:RELIANCE").Nth('W', new DateOnly(2025, 6, 10), 1).ShouldBeNull();
        Of("NSE:RELIANCE").Nth('M', new DateOnly(2025, 6, 10), 1)!.Date.ShouldBe(new DateOnly(2025, 6, 26));
        Of().Nth('X', new DateOnly(2025, 6, 10), 1).ShouldBeNull();
    }

    [Fact]
    public void The_stored_epoch_row_moves_the_era_boundary()
    {
        var later = new MarketEpoch(ExpiryRules.TuesdayEpoch, new DateOnly(2025, 10, 1), "later", null, false);

        Of(epochs: [later]).Nth('W', new DateOnly(2025, 9, 1), 1)!.Date.ShouldBe(new DateOnly(2025, 9, 4));
    }
}
