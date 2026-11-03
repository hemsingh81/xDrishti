using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options.Iv;
using XDrishti.Domain.Options.Remark;
using static XDrishti.Domain.Tests.Options.Iv.IvKit;

namespace XDrishti.Domain.Tests.Options.Iv;

/// <summary>P14-11: the front series is the nearest with at least three sessions to expiry; the back series is the next code when the archive holds it.</summary>
public sealed class IvSeriesTests
{
    [Fact]
    public void Nifty_on_the_weekly_series_front_w1_and_back_w2_with_four_sessions_to_the_first_expiry()
    {
        var plan = IvSeries.Plan(ExpiryRules.Nifty, Wed, NiftyWeeklies(), Calendar);

        plan.ShouldBe(new IvSeriesPlan('W', 1, new DateOnly(2026, 8, 4), 2, 5));
    }

    [Fact]
    public void With_fewer_than_three_sessions_to_the_first_expiry_the_second_is_the_front_and_there_is_no_back()
    {
        var friday = new DateOnly(2026, 7, 31);

        var plan = IvSeries.Plan(ExpiryRules.Nifty, friday, NiftyWeeklies(), Calendar);

        plan.ShouldBe(new IvSeriesPlan('W', 2, new DateOnly(2026, 8, 11), null, 5));
    }

    [Fact]
    public void Exactly_three_sessions_is_enough_and_two_is_not()
    {
        // trade date Friday 07-31: sessions to Tue 08-04 are Mon, Tue = 2 (not enough); Thursday 07-30: Fri, Mon, Tue = 3 (enough)
        IvSeries.Plan(ExpiryRules.Nifty, new DateOnly(2026, 7, 30), NiftyWeeklies(), Calendar)!.FrontCode.ShouldBe(1);
        IvSeries.Plan(ExpiryRules.Nifty, new DateOnly(2026, 7, 31), NiftyWeeklies(), Calendar)!.FrontCode.ShouldBe(2);
    }

    [Fact]
    public void Banknifty_is_on_the_monthly_series_with_a_second_code_and_a_share_has_one_code_only()
    {
        var bank = Expiries(ExpiryRules.BankNifty, (new DateOnly(2026, 8, 25), 'M'), (new DateOnly(2026, 9, 29), 'M'));
        var share = Expiries("NSE:RELIANCE", (new DateOnly(2026, 8, 25), 'M'), (new DateOnly(2026, 9, 29), 'M'));

        IvSeries.Plan(ExpiryRules.BankNifty, Wed, bank, Calendar).ShouldBe(new IvSeriesPlan('M', 1, new DateOnly(2026, 8, 25), 2, 5));
        IvSeries.Plan("NSE:RELIANCE", Wed, share, Calendar).ShouldBe(new IvSeriesPlan('M', 1, new DateOnly(2026, 8, 25), null, 15));
    }

    [Fact]
    public void A_share_close_to_its_expiry_has_no_series_and_an_unknown_expiry_has_none()
    {
        var share = Expiries("NSE:RELIANCE", (new DateOnly(2026, 8, 25), 'M'), (new DateOnly(2026, 9, 29), 'M'));

        IvSeries.Plan("NSE:RELIANCE", new DateOnly(2026, 8, 24), share, Calendar).ShouldBeNull("one held code and one session left");
        var closed = new TradingCalendar([.. Enumerable.Range(0, 500).Select(i => TradingDay.Holiday(new DateOnly(2026, 6, 1).AddDays(i), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch))]);
        IvSeries.Plan("NSE:RELIANCE", Wed, new ExpiryCalendar("NSE:RELIANCE", [], [], closed), closed).ShouldBeNull("no expiry date can be given");
    }
}
