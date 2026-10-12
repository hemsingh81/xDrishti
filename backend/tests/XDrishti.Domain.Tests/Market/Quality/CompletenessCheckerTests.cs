using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;
using static XDrishti.Domain.Tests.Market.Quality.QualityFixtures;

namespace XDrishti.Domain.Tests.Market.Quality;

public sealed class CompletenessCheckerTests
{
    private static readonly IReadOnlySet<DateOnly> None = new HashSet<DateOnly>();

    private static (CompletenessCheck Check, IReadOnlyList<MissingRange> Missing) Check(
        IReadOnlyList<Bar> bars, TradingCalendar? calendar = null, DateTimeOffset? firstBar = null, IReadOnlySet<DateOnly>? unfillable = null, IReadOnlyList<DateOnly>? sessions = null) =>
        CompletenessChecker.Check(calendar ?? new TradingCalendar(), sessions ?? Window, firstBar ?? DateTimeOffset.FromUnixTimeSeconds(0), bars, unfillable ?? None, 1);

    [Fact]
    public void A_window_with_every_minute_passes_with_nothing_missing()
    {
        var (check, missing) = Check(FullWindow());

        (check.Status, check.ExpectedMinutes, check.FoundMinutes, check.MissingMinutes, check.MissingPercent).ShouldBe((CheckStatus.Pass, 1875, 1875, 0, 0));
        check.Sessions.Count.ShouldBe(5);
        missing.ShouldBeEmpty();
    }

    [Fact]
    public void A_short_gap_is_listed_as_a_range_and_below_the_limit_it_still_passes()
    {
        var bars = Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is >= 600 and <= 602)).ToList();

        var (check, missing) = Check(bars);

        (check.Status, check.MissingMinutes, check.MissingPercent).ShouldBe((CheckStatus.Pass, 3, 0.16));
        missing.ShouldBe([new MissingRange(Wed, 600, 3)]);
        check.Sessions.Single(s => s.Date == Wed).ShouldBe(new SessionCompleteness(Wed, 375, 372, 3, false));
    }

    [Fact]
    public void More_than_the_limit_missing_is_a_warning_never_a_failure()
    {
        var bars = Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is >= 600 and < 630)).ToList();

        var (check, _) = Check(bars);

        (check.Status, check.MissingMinutes, check.MissingPercent).ShouldBe((CheckStatus.Warning, 30, 1.6));
    }

    [Fact]
    public void Separate_gaps_are_separate_ranges_in_time_order()
    {
        var bars = Sessions(Mon, Tue, Fri).Concat(Session(Wed, m => m == 555)).Concat(Session(Thu, m => m is 700 or 701 or 800)).ToList();

        var (_, missing) = Check(bars);

        missing.ShouldBe([new MissingRange(Wed, 555, 1), new MissingRange(Thu, 700, 2), new MissingRange(Thu, 800, 1)]);
    }

    [Fact]
    public void A_missing_day_is_every_minute_of_it()
    {
        var (check, missing) = Check(Sessions(Mon, Tue, Wed, Thu));

        (check.MissingMinutes, check.Status).ShouldBe((375, CheckStatus.Warning));
        missing.ShouldBe([new MissingRange(Fri, 555, 375)]);
    }

    [Fact]
    public void Missing_minutes_of_a_day_a_refetch_could_not_fill_are_a_warning_even_below_the_limit()
    {
        var bars = Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is 600 or 601)).ToList();

        var (check, _) = Check(bars, unfillable: new HashSet<DateOnly> { Wed });

        (check.Status, check.MissingMinutes, check.UnfillableMinutes).ShouldBe((CheckStatus.Warning, 2, 2));
        check.Sessions.Single(s => s.Date == Wed).Unfillable.ShouldBeTrue();
    }

    [Fact]
    public void An_unfillable_day_that_is_complete_now_is_not_flagged()
    {
        var (check, _) = Check(FullWindow(), unfillable: new HashSet<DateOnly> { Wed });

        (check.Status, check.UnfillableMinutes).ShouldBe((CheckStatus.Pass, 0));
        check.Sessions.ShouldAllBe(s => !s.Unfillable);
    }

    [Fact]
    public void Sessions_up_to_the_day_of_the_first_bar_are_not_held_against_the_instrument()
    {
        var firstBar = DateTimeOffset.FromUnixTimeSeconds(Epoch(Wed, 600));

        var (check, _) = Check(Sessions(Thu, Fri), firstBar: firstBar);

        (check.ExpectedMinutes, check.MissingMinutes, check.Status).ShouldBe((750, 0, CheckStatus.Pass));
        check.Sessions.Select(s => s.Date).ShouldBe([Thu, Fri]);
    }

    [Fact]
    public void A_holiday_has_no_expected_minutes()
    {
        var calendar = new TradingCalendar([TradingDay.Holiday(Wed, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);

        var (check, _) = Check(Sessions(Mon, Tue, Thu, Fri), calendar);

        (check.ExpectedMinutes, check.MissingMinutes).ShouldBe((1500, 0));
    }

    [Fact]
    public void A_special_session_expects_the_minutes_of_its_own_window()
    {
        var muhurat = new DateOnly(2026, 11, 8);
        var calendar = new TradingCalendar([TradingDay.Special(muhurat, Minute(18, 15), Minute(19, 15), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);
        var bars = Enumerable.Range(Minute(18, 15), 58).Select(m => Calm(muhurat, m)).ToList();

        var (check, missing) = CompletenessChecker.Check(calendar, [muhurat], DateTimeOffset.FromUnixTimeSeconds(0), bars, None, 1);

        (check.ExpectedMinutes, check.MissingMinutes).ShouldBe((60, 2));
        missing.ShouldBe([new MissingRange(muhurat, Minute(19, 13), 2)]);
    }

    [Fact]
    public void Bars_outside_the_session_do_not_count_as_found()
    {
        var bars = FullWindow().Concat([Calm(Wed, Minute(15, 30)), Calm(Wed, Minute(9, 0))]).ToList();

        var (check, _) = Check(bars);

        check.FoundMinutes.ShouldBe(1875);
    }

    [Fact]
    public void The_list_of_ranges_is_capped_but_the_totals_are_not()
    {
        var bars = Window.SelectMany(date => Session(date, m => m % 2 == 0)).ToList(); // every other minute is missing: 187 ranges a day

        var (check, missing) = Check(bars);

        check.MissingMinutes.ShouldBe(5 * 187);
        missing.Count.ShouldBe(CompletenessChecker.MaxRanges);
        missing[0].ShouldBe(new MissingRange(Mon, 556, 1));
    }
}
