using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;
using static XDrishti.Domain.Tests.Market.Quality.QualityFixtures;

namespace XDrishti.Domain.Tests.Market.Quality;

/// <summary>The checks put together on hand-made instruments: one that is clean and one with each kind of trouble.</summary>
public sealed class QualityEngineTests
{
    [Fact]
    public void A_clean_instrument_passes_every_check_and_scores_one_hundred()
    {
        var outcome = QualityEngine.Evaluate(Input(FullWindow()), Thresholds);

        (outcome.Status, outcome.Score, outcome.BarsChecked, outcome.MissingMinutes).ShouldBe((CheckStatus.Pass, 100, 1875, 0));
        var checks = outcome.Checks;
        new[] { checks.Completeness.Status, checks.Sanity.Status, checks.Spikes.Status, checks.Aggregates.Status, checks.Freshness.Status }.ShouldAllBe(s => s == CheckStatus.Pass);
        checks.OfficialDaily.Status.ShouldBe(CheckStatus.NotAvailable);
        checks.Details!.Missing.ShouldBeEmpty();
        checks.Details.Differences.ShouldBeEmpty();
    }

    [Fact]
    public void A_gap_is_a_warning()
    {
        var bars = Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is >= 600 and < 650)).ToList();

        var outcome = QualityEngine.Evaluate(Input(bars), Thresholds);

        (outcome.Status, outcome.MissingMinutes).ShouldBe((CheckStatus.Warning, 50));
        outcome.Checks.Details!.Missing.ShouldBe([new MissingRange(Wed, 600, 50)]);
    }

    [Fact]
    public void A_bad_bar_fails_the_instrument()
    {
        var bars = FullWindow();
        bars[100] = bars[100] with { High = bars[100].Low - 1 };

        var outcome = QualityEngine.Evaluate(Input(bars), Thresholds);

        (outcome.Status, outcome.Checks.Sanity.BadBars).ShouldBe((CheckStatus.Fail, 1));
    }

    [Fact]
    public void A_tampered_aggregate_fails_the_instrument_and_lists_the_bucket()
    {
        var bars = FullWindow();
        var stored = StoredFrom(bars);
        var bucket = stored.First(b => b.Timeframe == Timeframe.D1 && b.Start == Epoch(Tue, 0));
        stored[stored.IndexOf(bucket)] = bucket with { Volume = bucket.Volume + 7 };

        var outcome = QualityEngine.Evaluate(Input(bars, stored), Thresholds);

        (outcome.Status, outcome.Checks.Aggregates.Status).ShouldBe((CheckStatus.Fail, CheckStatus.Fail));
        outcome.Checks.Details!.Differences.ShouldHaveSingleItem().Fields.ShouldBe(["volume"]);
        outcome.Score.ShouldBe(75);
    }

    [Fact]
    public void A_stale_instrument_fails_on_freshness()
    {
        var bars = Sessions(Mon, Tue, Wed, Thu);

        var outcome = QualityEngine.Evaluate(Input(bars), Thresholds);

        (outcome.Status, outcome.Checks.Freshness.Status, outcome.Checks.Freshness.DaysBehind).ShouldBe((CheckStatus.Fail, CheckStatus.Fail, 1));
        outcome.Checks.Completeness.Status.ShouldBe(CheckStatus.Warning); // the missing Friday is also missing minutes
    }

    [Fact]
    public void A_spike_that_reverses_is_a_warning()
    {
        var bars = FullWindow();
        var index = bars.FindIndex(b => b.EpochSeconds == Epoch(Wed, Minute(11, 0)));
        bars[index] = Of(Wed, Minute(11, 0), 100, 106, 100, 105);
        bars[index + 1] = Of(Wed, Minute(11, 1), 105, 105, 100, 100);

        var outcome = QualityEngine.Evaluate(Input(bars), Thresholds);

        (outcome.Status, outcome.Checks.Spikes.Spikes).ShouldBe((CheckStatus.Warning, 1));
    }

    [Fact]
    public void An_unfillable_day_is_a_warning_with_its_minutes_counted()
    {
        var bars = Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is 700 or 701)).ToList();

        var outcome = QualityEngine.Evaluate(Input(bars, unfillable: new HashSet<DateOnly> { Wed }), Thresholds);

        (outcome.Status, outcome.Checks.Completeness.UnfillableMinutes).ShouldBe((CheckStatus.Warning, 2));
    }

    [Fact]
    public void A_holiday_in_the_calendar_is_not_expected_and_not_a_gap()
    {
        var calendar = new TradingCalendar([TradingDay.Holiday(Wed, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);

        var outcome = QualityEngine.Evaluate(Input(Sessions(Mon, Tue, Thu, Fri), calendar: calendar), Thresholds);

        (outcome.Status, outcome.Checks.Completeness.ExpectedMinutes).ShouldBe((CheckStatus.Pass, 1500));
    }

    [Fact]
    public void Bars_before_the_window_are_read_for_the_week_and_month_but_not_checked_as_the_window()
    {
        var earlier = new DateOnly(2026, 10, 1);
        var bars = Sessions(earlier).Concat(FullWindow()).ToList();

        var outcome = QualityEngine.Evaluate(Input(bars), Thresholds);

        (outcome.Status, outcome.BarsChecked).ShouldBe((CheckStatus.Pass, 1875));
    }

    [Fact]
    public void The_thresholds_come_from_the_caller()
    {
        var bars = Sessions(Mon, Tue, Thu, Fri).Concat(Session(Wed, m => m is >= 600 and < 604)).ToList();

        var strict = QualityEngine.Evaluate(Input(bars), new QualityThresholds(20, 0.1));
        var lenient = QualityEngine.Evaluate(Input(bars), new QualityThresholds(20, 1));

        (strict.Checks.Completeness.Status, lenient.Checks.Completeness.Status).ShouldBe((CheckStatus.Warning, CheckStatus.Pass));
    }

    [Fact]
    public void An_empty_window_cannot_be_evaluated()
    {
        Should.Throw<ArgumentException>(() => QualityEngine.Evaluate(Input([]) with { Sessions = [] }, Thresholds));
    }

    [Fact]
    public void The_window_is_the_last_closed_sessions_of_the_calendar()
    {
        QualityWindow.Sessions(new TradingCalendar(), Now, 5).ShouldBe(Window);
        QualityWindow.Sessions(new TradingCalendar(), new DateTimeOffset(2026, 10, 12, 15, 30, 0, TimeSpan.FromMinutes(330)), 2).ShouldBe([Fri, new DateOnly(2026, 10, 12)]);
    }

    [Fact]
    public void A_holiday_is_not_a_session_of_the_window_and_the_window_reaches_one_further_back()
    {
        var calendar = new TradingCalendar([TradingDay.Holiday(Wed, CalendarSource.Manual, CalendarStatus.Confirmed, null, Now)]);

        QualityWindow.Sessions(calendar, Now, 5).ShouldBe([new DateOnly(2026, 10, 2), Mon, Tue, Thu, Fri]);
    }

    [Fact]
    public void The_bounds_reach_back_to_the_monday_and_the_first_of_the_month()
    {
        var bounds = QualityWindow.Bounds(Wed);

        bounds.ShouldBe(new QualityBounds(Epoch(Wed, 0), Epoch(Mon, 0), Epoch(new DateOnly(2026, 10, 1), 0)));
        bounds.ReadFrom.ShouldBe(Epoch(new DateOnly(2026, 10, 1), 0));
        QualityWindow.Bounds(Fri).ReadFrom.ShouldBe(Epoch(new DateOnly(2026, 10, 1), 0));
    }
}
