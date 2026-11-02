using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Tests.Filings;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P15-05 criterion 6 (frame features, the series rule): rows and filings count from the start of the India date, so a meeting intimated at 11:00 counts from the next date, never earlier than the instant rule.</summary>
public sealed class ResultsDaySeriesTests
{
    private static readonly TradingCalendar Plain = new();

    private static readonly CalendarRow[] Meeting = [Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0))];

    private static ResultsDaySeries Build(CalendarRow[] rows, ResultsFilingTime[]? filings = null, TradingCalendar? calendar = null) =>
        ResultsDaySeries.Build(rows, filings ?? [], Lag, calendar ?? Plain, new DateOnly(2026, 7, 20), new DateOnly(2026, 8, 20));

    [Fact]
    public void The_window_table_row_gives_three_on_the_seventh_and_nan_on_the_third_when_the_intimation_was_that_morning()
    {
        var series = Build(Meeting).Points;

        series[new DateOnly(2026, 8, 7)].DaysToResults.ShouldBe(3);
        series[new DateOnly(2026, 8, 3)].DaysToResults.ShouldBeNaN("intimated at 11:00 on the third: visible from the start of the fourth");
        series[new DateOnly(2026, 8, 4)].DaysToResults.ShouldBe(6);
        series[new DateOnly(2026, 8, 12)].DaysToResults.ShouldBe(0);
        series[new DateOnly(2026, 8, 13)].DaysToResults.ShouldBeNaN("the meeting is past and no other row is upcoming");
        series.ContainsKey(new DateOnly(2026, 8, 8)).ShouldBeFalse("a Saturday has no session and no entry");
    }

    [Fact]
    public void The_frame_value_is_never_earlier_than_the_instant_rule_so_it_only_sees_less()
    {
        var snapshot = EventCalendarSnapshot.Create(Meeting, 5);
        var series = Build(Meeting).Points;

        foreach (var (date, point) in series)
        {
            var atOpen = snapshot.Answer(FilingSamples.Instrument, new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), Ist).AddHours(9).AddMinutes(15), Plain);
            if (!double.IsNaN(point.DaysToResults))
            {
                atOpen.Days.ShouldBe((int)point.DaysToResults, date.ToString());
            }
        }
    }

    [Fact]
    public void Days_since_results_follows_the_reaction_session_of_the_newest_visible_filing()
    {
        var filing = new ResultsFilingTime(FilingSamples.Instrument, Q1, new DateTimeOffset(2026, 7, 24, 16, 12, 0, Ist).ToUniversalTime());

        var series = Build([], [filing]).Points;

        series[new DateOnly(2026, 7, 24)].DaysSinceResults.ShouldBeNaN("broadcast at 16:12: not visible at the start of the date");
        series[new DateOnly(2026, 7, 27)].DaysSinceResults.ShouldBe(0, "the start of Monday: visible, and Monday is the reaction session");
        series[new DateOnly(2026, 7, 28)].DaysSinceResults.ShouldBe(1);
        series[new DateOnly(2026, 8, 3)].DaysSinceResults.ShouldBe(5);
        series[new DateOnly(2026, 7, 27)].DaysToResults.ShouldBeNaN("no calendar row: unknown, not far away");
    }

    [Fact]
    public void A_filing_broadcast_in_the_morning_reacts_next_session_and_the_series_waits_for_it()
    {
        var morning = new ResultsFilingTime(FilingSamples.Instrument, Q1, new DateTimeOffset(2026, 7, 27, 11, 0, 0, Ist).ToUniversalTime());

        var series = Build([], [morning]).Points;

        series[new DateOnly(2026, 7, 27)].DaysSinceResults.ShouldBeNaN();
        series[new DateOnly(2026, 7, 28)].DaysSinceResults.ShouldBe(0, "visible from the start of Tuesday, which is the reaction session");
        series[new DateOnly(2026, 7, 29)].DaysSinceResults.ShouldBe(1);
    }

    [Fact]
    public void A_newer_filing_replaces_the_older_one_from_its_own_date_and_the_count_restarts()
    {
        var first = new ResultsFilingTime(FilingSamples.Instrument, Q1, new DateTimeOffset(2026, 7, 24, 16, 12, 0, Ist).ToUniversalTime());
        var second = new ResultsFilingTime(FilingSamples.Instrument, new DateOnly(2026, 9, 30), new DateTimeOffset(2026, 8, 10, 12, 0, 0, Ist).ToUniversalTime());

        var series = Build([], [second, first]).Points;

        series[new DateOnly(2026, 8, 10)].DaysSinceResults.ShouldBe(10, "still the first filing at the start of the tenth");
        series[new DateOnly(2026, 8, 11)].DaysSinceResults.ShouldBe(0, "the second filing is visible from the start of the eleventh, its reaction session");
    }

    [Fact]
    public void An_empty_input_gives_nan_on_every_trading_date()
    {
        var series = Build([]).Points;

        series.Count.ShouldBe(24);
        series.Values.ShouldAllBe(p => double.IsNaN(p.DaysToResults) && double.IsNaN(p.DaysSinceResults));
    }

    [Fact]
    public void The_inputs_hash_and_emptiness_are_stable_for_the_same_filings_in_any_order()
    {
        var a = new ResultsFilingTime(FilingSamples.Instrument, Q1, At(7, 24, 16, 12).ToUniversalTime());
        var b = new ResultsFilingTime(FilingSamples.Instrument, new DateOnly(2026, 9, 30), At(8, 10, 12, 0).ToUniversalTime());

        var one = ResultsDayInputs.Create(null, [a, b], 5);
        var two = ResultsDayInputs.Create(null, [b, a, a], 5);

        one.FilingsSha256.ShouldBe(two.FilingsSha256);
        one.FilingCount.ShouldBe(2);
        one.IsEmpty.ShouldBeFalse();
        ResultsDayInputs.Create(null, [], 5).IsEmpty.ShouldBeTrue();
        ResultsDayInputs.Create(EventCalendarSnapshot.Create([], 5), [], 5).IsEmpty.ShouldBeTrue();
        ResultsDayInputs.Create(EventCalendarSnapshot.Create(Meeting, 5), [], 5).IsEmpty.ShouldBeFalse();
        ResultsDayInputs.Create(null, [a], 5).FilingsSha256.ShouldNotBe(one.FilingsSha256);
    }

    [Fact]
    public void The_series_of_214_instruments_over_five_years_has_a_point_for_every_trading_date_and_visits_each_date_once()
    {
        // 214 instruments, 5 years of dates, a results meeting and a filing every quarter. Work done, not wall-clock: a point for every trading date of every instrument (the time bound is the non-functional note of the spec,
        // measured by hand at about a second for this size)
        var from = new DateOnly(2021, 1, 1);
        var to = new DateOnly(2025, 12, 31);
        var rows = new List<CalendarRow>();
        var filings = new List<ResultsFilingTime>();
        for (var q = 0; q < 20; q++)
        {
            var meeting = from.AddMonths((3 * q) + 1).AddDays(14);
            rows.Add(Row(q + 1, meeting, new DateTimeOffset(meeting.AddDays(-7).ToDateTime(TimeOnly.MinValue), Ist).AddHours(11), period: from.AddMonths(3 * q)));
            filings.Add(new ResultsFilingTime(FilingSamples.Instrument, from.AddMonths(3 * q), new DateTimeOffset(meeting.ToDateTime(TimeOnly.MinValue), Ist).AddHours(16)));
        }

        var expected = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Count(d => Plain.IsTradingDay(from.AddDays(d)));
        var points = 0;
        for (var i = 0; i < 214; i++)
        {
            var series = ResultsDaySeries.Build(rows, filings, Lag, Plain, from, to);
            points += series.Points.Count;
            series.Points.Values.Count(p => !double.IsNaN(p.DaysToResults)).ShouldBeGreaterThan(80);
            series.Points.Values.Count(p => !double.IsNaN(p.DaysSinceResults)).ShouldBeGreaterThan(1000);
        }

        points.ShouldBe(214 * expected);
    }
}
