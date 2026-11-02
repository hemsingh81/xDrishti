using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>
/// P15-04 criteria 2 to 5 (the point of the slice): the days rule on the hand cases of the spec (Friday 2026-08-07 is a session, 08-08 and 08-09 the weekend), the look-ahead rule through the intimation time and the lag, reschedules, cancellations and the
/// closed reasons of an unavailable answer.
/// </summary>
public sealed class ResultsDaysTests
{
    private static readonly TradingCalendar Plain = new();

    private static readonly DateTimeOffset Intimated = At(8, 3, 11, 0);

    private static ResultsAnswer Days(IEnumerable<CalendarRow> rows, DateTimeOffset t, TradingCalendar? calendar = null) => ResultsDays.Compute(rows, t, Lag, calendar ?? Plain);

    private static CalendarRow Meeting(DateOnly date) => Row(1, date, Intimated);

    [Theory]
    [InlineData(8, 7, 10, 3)]
    [InlineData(8, 10, 10, 2)]
    [InlineData(8, 11, 10, 1)]
    [InlineData(8, 11, 23, 1)]
    [InlineData(8, 12, 0, 0)]
    [InlineData(8, 12, 15, 0)]
    public void A_meeting_on_wednesday_2026_08_12_counts_sessions_from_the_india_date_of_the_question(int month, int day, int hour, int days)
    {
        var answer = Days([Meeting(new DateOnly(2026, 8, 12))], At(month, day, hour, 0));

        (answer.State, answer.Days, answer.EventSession, answer.MeetingDate, answer.PeriodEnd).ShouldBe(("value", days, new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12), Q1));
    }

    [Fact]
    public void A_late_evening_question_in_utc_that_is_already_the_next_day_in_india_counts_the_india_date()
    {
        var t = new DateTimeOffset(2026, 8, 11, 19, 0, 0, TimeSpan.Zero); // 00:30 IST on 12 August

        Days([Meeting(new DateOnly(2026, 8, 12))], t).Days.ShouldBe(0);
    }

    [Fact]
    public void A_saturday_meeting_has_its_event_session_on_the_next_monday()
    {
        var answer = Days([Meeting(new DateOnly(2026, 8, 8))], At(8, 7, 10));

        (answer.Days, answer.MeetingDate, answer.EventSession).ShouldBe((1, new DateOnly(2026, 8, 8), new DateOnly(2026, 8, 10)));
    }

    [Fact]
    public void A_meeting_on_a_holiday_has_its_event_session_on_the_next_session()
    {
        var answer = Days([Meeting(new DateOnly(2026, 8, 12))], At(8, 7, 10), WithHoliday(8, 12));

        (answer.Days, answer.EventSession).ShouldBe((3, new DateOnly(2026, 8, 13)));
    }

    [Fact]
    public void Nothing_counts_before_the_intimation_time_plus_the_lag_and_it_counts_from_that_instant_on()
    {
        var rows = new[] { Meeting(new DateOnly(2026, 8, 12)) };

        var before = Days(rows, At(8, 3, 11, 4, 59));
        var at = Days(rows, At(8, 3, 11, 5, 0));

        (before.State, before.Reason, before.Days).ShouldBe(("unavailable", "no_entry", null));
        (at.State, at.Days).ShouldBe(("value", 7));
    }

    [Fact]
    public void A_meeting_intimated_after_the_question_never_contributes_even_when_its_date_is_the_next_session()
    {
        var late = Row(1, new DateOnly(2026, 8, 10), At(8, 7, 15, 0));
        var known = Row(2, new DateOnly(2026, 8, 20), Intimated, period: new DateOnly(2026, 9, 30));

        var answer = Days([late, known], At(8, 7, 10));

        (answer.EventSession, answer.Days).ShouldBe((new DateOnly(2026, 8, 20), 9));
    }

    [Fact]
    public void A_reschedule_replaces_the_earlier_date_only_from_its_own_visible_time()
    {
        var first = Row(1, new DateOnly(2026, 8, 12), Intimated);
        var moved = Row(2, new DateOnly(2026, 8, 20), At(8, 5, 10, 0), MeetingRow.Rescheduled, supersedes: first.Filing.Id);
        var rows = new[] { first, moved };

        var before = Days(rows, At(8, 5, 10, 4, 59));
        var after = Days(rows, At(8, 5, 10, 5, 0));

        before.EventSession.ShouldBe(new DateOnly(2026, 8, 12));
        after.EventSession.ShouldBe(new DateOnly(2026, 8, 20));
        ResultsDays.Upcoming(rows, At(8, 6, 10), Lag, Plain).ShouldHaveSingleItem().Revision.ShouldBe(2);
        ResultsDays.Upcoming(rows, At(8, 5, 10, 4), Lag, Plain).ShouldHaveSingleItem().Revision.ShouldBe(1);
    }

    [Fact]
    public void A_cancelled_row_makes_the_answer_unavailable_from_its_own_time_until_a_later_scheduled_row()
    {
        var first = Row(1, new DateOnly(2026, 8, 12), Intimated);
        var cancelled = Row(2, new DateOnly(2026, 8, 12), At(8, 5, 10, 0), MeetingRow.Cancelled, supersedes: first.Filing.Id);
        var again = Row(3, new DateOnly(2026, 8, 18), At(8, 6, 10, 0), supersedes: cancelled.Filing.Id);
        var rows = new[] { first, cancelled, again };

        Days(rows, At(8, 5, 9, 0)).Days.ShouldBe(5);
        Days(rows, At(8, 5, 10, 5)).Reason.ShouldBe("only_cancelled");
        Days(rows, At(8, 6, 10, 5)).EventSession.ShouldBe(new DateOnly(2026, 8, 18));
    }

    [Fact]
    public void A_row_stored_out_of_order_is_visible_by_its_own_time_and_does_not_hide_the_newer_one()
    {
        var older = Row(1, new DateOnly(2026, 8, 12), At(8, 1, 10, 0));
        var newer = Row(2, new DateOnly(2026, 8, 20), At(8, 5, 10, 0), supersedes: null);
        var between = Row(3, new DateOnly(2026, 8, 14), At(8, 3, 10, 0));

        Days([older, newer, between], At(8, 4, 10, 0)).EventSession.ShouldBe(new DateOnly(2026, 8, 14));
        Days([older, newer, between], At(8, 6, 10, 0)).EventSession.ShouldBe(new DateOnly(2026, 8, 20));
    }

    [Fact]
    public void No_row_gives_no_entry_and_never_zero()
    {
        var answer = Days([], At(8, 7, 10));

        (answer.State, answer.Reason, answer.Days, answer.MeetingDate, answer.EventSession).ShouldBe(("unavailable", "no_entry", null, null, null));
    }

    [Fact]
    public void A_row_without_an_intimation_time_never_counts()
    {
        Days([Row(1, new DateOnly(2026, 8, 12), null)], At(8, 7, 10)).Reason.ShouldBe("no_entry");
    }

    [Fact]
    public void Only_a_meeting_whose_event_session_is_past_gives_no_upcoming_entry()
    {
        var answer = Days([Meeting(new DateOnly(2026, 8, 12))], At(8, 13, 10));

        (answer.State, answer.Reason).ShouldBe(("unavailable", "no_upcoming_entry"));
    }

    [Fact]
    public void Other_purposes_are_stored_but_never_feed_the_days()
    {
        Days([Row(1, new DateOnly(2026, 8, 12), Intimated, purpose: MeetingRow.Other)], At(8, 7, 10)).Reason.ShouldBe("no_entry");
    }

    [Fact]
    public void The_earliest_upcoming_period_wins_and_a_cancelled_one_is_skipped()
    {
        var cancelled = Row(1, new DateOnly(2026, 8, 10), Intimated, MeetingRow.Cancelled, period: new DateOnly(2026, 3, 31));
        var later = Row(2, new DateOnly(2026, 11, 10), Intimated, period: new DateOnly(2026, 9, 30));
        var sooner = Row(3, new DateOnly(2026, 8, 12), Intimated, period: Q1);

        var answer = Days([later, cancelled, sooner], At(8, 7, 10));

        answer.PeriodEnd.ShouldBe(Q1);
    }

    [Fact]
    public void A_calendar_that_knows_no_session_for_a_year_gives_no_event_session_and_the_row_is_skipped()
    {
        var holidays = Enumerable.Range(0, 500).Select(i => TradingDay.Holiday(new DateOnly(2026, 8, 1).AddDays(i), CalendarSource.Manual, CalendarStatus.Confirmed, null, FilingSamples.Ingested));

        ResultsDays.EventSession(new TradingCalendar(holidays), new DateOnly(2026, 8, 12)).ShouldBeNull();
        Days([Meeting(new DateOnly(2026, 8, 12))], At(8, 7, 10), new TradingCalendar(holidays)).Reason.ShouldBe("no_upcoming_entry");
    }

    [Fact]
    public void The_revision_counts_the_earlier_visible_rows_of_the_period_whatever_the_order_they_were_linked_in()
    {
        var a = Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0));
        var c = Row(3, new DateOnly(2026, 8, 20), At(8, 6, 10, 0), MeetingRow.Rescheduled, supersedes: a.Filing.Id); // linked to a although b is between them
        var b = Row(2, new DateOnly(2026, 8, 14), At(8, 4, 10, 0), MeetingRow.Rescheduled);

        ResultsDays.Upcoming([a, b, c], At(8, 7, 10), Lag, Plain).ShouldHaveSingleItem().Revision.ShouldBe(3);
        ResultsDays.Upcoming([c, b, a], At(8, 7, 10), Lag, Plain).ShouldHaveSingleItem().Revision.ShouldBe(3);
        ResultsDays.Upcoming([a, b, c], At(8, 5, 10), Lag, Plain).ShouldHaveSingleItem().Revision.ShouldBe(2);
    }
}
