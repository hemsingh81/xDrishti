using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Tests.Market.Context;

/// <summary>P3-01d criterion 2 and the hand fixture of the spec: the daily rows of NIFTY 50 and INDIA VIX to the records of the context.</summary>
public sealed class ContextDerivationTests
{
    private static readonly DateOnly S1 = new(2026, 3, 2);

    private static readonly DateOnly[] Sessions = [S1, S1.AddDays(1), S1.AddDays(2), S1.AddDays(3), S1.AddDays(4), S1.AddDays(7)];

    private static readonly ContextDerivationOptions Grace2 = new(2, 0.96);

    private static readonly IReadOnlySet<DateOnly> None = new HashSet<DateOnly>();

    private static DailyLeg Leg(DateOnly date, double close, int bars = 375) => new(date, close + 5, close - 5, close, bars, bars == 375);

    private static List<DailyLeg> Index(params DateOnly[] dates) => [.. dates.Select((d, i) => Leg(d, 22_000 + i * 10))];

    private static ContextDerivationResult Derive(IEnumerable<DailyLeg> index, IEnumerable<DailyLeg> vix, TradingCalendar? calendar = null, IReadOnlySet<DateOnly>? existing = null, DateOnly? lastClosed = null, ContextDerivationOptions? options = null) =>
        ContextDerivation.Derive(index, vix, calendar ?? new TradingCalendar(), lastClosed ?? Sessions[^1], existing ?? None, options ?? Grace2);

    [Fact]
    public void The_hand_fixture_gives_four_records_two_pending_and_no_skip()
    {
        var vix = new[] { Leg(Sessions[0], 15.0), Leg(Sessions[2], 16.0), Leg(Sessions[3], 14.0) };

        var result = Derive(Index(Sessions), vix);

        result.Records.Select(r => (r.Date, r.VixClose)).ShouldBe([(Sessions[0], 15.0), (Sessions[1], (double?)null), (Sessions[2], 16.0), (Sessions[3], 14.0)]);
        result.Pending.ShouldBe([Sessions[4], Sessions[5]]);
        result.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public void A_thin_vix_leg_is_treated_as_missing_and_its_bars_are_listed()
    {
        var vix = new[] { Leg(Sessions[0], 15.0), Leg(Sessions[2], 16.0), Leg(Sessions[3], 14.0, bars: 340) };

        var result = Derive(Index(Sessions), vix);

        result.Records.Single(r => r.Date == Sessions[3]).VixClose.ShouldBeNull("age 2 has reached the grace of 2, so the day goes without");
        result.Skipped.ShouldBe([new SkippedContextDay(Sessions[3], ContextSkipReason.VixThin, 340)]);
    }

    [Fact]
    public void A_thin_vix_leg_of_a_recent_day_waits()
    {
        var vix = new[] { Leg(Sessions[4], 14.0, bars: 340) };

        var result = Derive(Index(Sessions), vix);

        result.Pending.ShouldContain(Sessions[4]);
        result.Records.ShouldNotContain(r => r.Date == Sessions[4]);
    }

    [Fact]
    public void A_day_with_a_thin_index_leg_is_skipped_with_its_bars_and_a_missing_one_is_skipped_as_missing()
    {
        var index = Index(Sessions).Where(l => l.Date != Sessions[1]).Select(l => l.Date == Sessions[2] ? l with { Bars = 300, Complete = false } : l);

        var result = Derive(index, Sessions.Select(d => Leg(d, 15)));

        result.Skipped.ShouldBe([new SkippedContextDay(Sessions[1], ContextSkipReason.IndexMissing, 0), new SkippedContextDay(Sessions[2], ContextSkipReason.IndexThin, 300)]);
        result.Records.Select(r => r.Date).ShouldBe([Sessions[0], Sessions[3], Sessions[4], Sessions[5]]);
    }

    [Fact]
    public void The_threshold_is_the_share_of_the_expected_minutes_and_exactly_96_percent_counts()
    {
        var result = Derive([Leg(S1, 22_000, 360), Leg(S1.AddDays(1), 22_000, 359), Leg(S1.AddDays(2), 22_000)], [], lastClosed: S1.AddDays(2), options: new ContextDerivationOptions(1, 0.96));

        result.Records.Select(r => r.Date).ShouldBe([S1]);
        result.Skipped.ShouldBe([new SkippedContextDay(S1.AddDays(1), ContextSkipReason.IndexThin, 359)]);
        result.Pending.ShouldBe([S1.AddDays(2)]);
    }

    [Fact]
    public void A_date_that_is_not_a_session_of_the_calendar_is_skipped_and_never_written()
    {
        var holiday = TradingDay.Holiday(Sessions[2], CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);
        var saturday = new DateOnly(2026, 3, 7);

        var result = Derive([.. Index(Sessions), Leg(saturday, 22_100)], Sessions.Select(d => Leg(d, 15)), new TradingCalendar([holiday]));

        result.Skipped.Select(s => (s.Date, s.Reason)).ShouldBe([(Sessions[2], ContextSkipReason.NotASession), (saturday, ContextSkipReason.NotASession)]);
        result.Records.ShouldNotContain(r => r.Date == Sessions[2] || r.Date == saturday);
    }

    [Fact]
    public void A_special_session_is_judged_by_its_own_length()
    {
        var evening = new DateOnly(2026, 11, 8);
        var special = TradingDay.Special(evening, 18 * 60, 19 * 60, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);

        var result = Derive([Leg(evening, 24_000, 60)], [Leg(evening, 14, 58)], new TradingCalendar([special]), lastClosed: new DateOnly(2026, 11, 9), options: new ContextDerivationOptions(1, 0.96));

        result.Records.ShouldHaveSingleItem().ShouldBe(new ContextDayRecord(evening, 24_005, 23_995, 24_000, 14, 60, 58));
    }

    [Fact]
    public void A_day_after_the_last_closed_session_is_never_written_and_not_even_listed()
    {
        var result = Derive(Index(Sessions), Sessions.Select(d => Leg(d, 15)), lastClosed: Sessions[3]);

        result.Records.Select(r => r.Date).ShouldBe(Sessions[..4]);
        result.Pending.ShouldBeEmpty();
        result.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public void A_stored_date_is_never_derived_again()
    {
        var result = Derive(Index(Sessions), Sessions.Select(d => Leg(d, 15)), existing: new HashSet<DateOnly>(Sessions[..3]));

        result.Records.Select(r => r.Date).ShouldBe(Sessions[3..]);
    }

    [Fact]
    public void Duplicate_and_unordered_rows_give_the_same_records_as_clean_ones()
    {
        var index = Index(Sessions);
        var vix = Sessions.Select(d => Leg(d, 15)).ToList();
        var clean = Derive(index, vix);

        var messy = Derive([.. index.AsEnumerable().Reverse(), index[2]], [.. vix.AsEnumerable().Reverse(), vix[1]]);

        messy.Records.ShouldBe(clean.Records);
    }

    [Fact]
    public void A_leg_that_is_not_a_possible_day_is_skipped_so_that_the_table_never_refuses_a_row()
    {
        var broken = Leg(Sessions[1], 22_000) with { High = 21_000 };

        var result = Derive(Index(Sessions).Select(l => l.Date == Sessions[1] ? broken : l), Sessions.Select(d => Leg(d, 15)));

        result.Skipped.ShouldBe([new SkippedContextDay(Sessions[1], ContextSkipReason.IndexInvalid, 375)]);
    }

    [Fact]
    public void Without_any_index_leg_nothing_is_derived()
    {
        var result = Derive([], Sessions.Select(d => Leg(d, 15)));

        result.Records.ShouldBeEmpty();
        result.Pending.ShouldBeEmpty();
        result.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public void The_last_closed_session_with_372_of_375_bars_is_pending_but_the_same_day_once_older_is_written()
    {
        var partial = Leg(S1, 22_000, 372);
        var vix = Leg(S1, 15, 372);

        var onTheLastClosed = Derive([partial], [vix], lastClosed: S1, options: new ContextDerivationOptions(1, 0.96));
        var older = Derive([partial], [vix], lastClosed: S1.AddDays(1), options: new ContextDerivationOptions(1, 0.96));
        var complete = Derive([partial with { Bars = 375, Complete = true }], [vix with { Bars = 375, Complete = true }], lastClosed: S1, options: new ContextDerivationOptions(1, 0.96));

        onTheLastClosed.Records.ShouldBeEmpty();
        onTheLastClosed.Pending.ShouldBe([S1]);
        onTheLastClosed.Skipped.ShouldBeEmpty();
        older.Records.ShouldHaveSingleItem().VixClose.ShouldBe(15);
        complete.Records.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    public void A_full_vix_leg_with_a_close_that_is_not_possible_is_listed_as_invalid_and_the_day_goes_without_it(double close)
    {
        var vix = new[] { Leg(Sessions[0], 15) with { Close = close } };

        var result = Derive(Index(Sessions[..1]), vix);

        result.Skipped.ShouldContain(new SkippedContextDay(Sessions[0], ContextSkipReason.VixInvalid, 375));
        result.Records.ShouldHaveSingleItem().VixClose.ShouldBeNull();
    }
}
