using XDrishti.Domain.Filings;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-06 criterion 7: the event instants of an instrument for the loss tag. A results filing is its own broadcast time; a calendar meeting without a filing for its period is the 09:15 India time of its event session; an instrument with neither is absent, never empty.</summary>
public sealed class ResultsEventsTests
{
    private static readonly Guid Who = FilingSamples.Instrument;

    private static readonly DateTimeOffset AsOf = At(12, 1, 10, 0);

    private static IReadOnlyList<EventInstant> Events(IEnumerable<CalendarRow> rows, IEnumerable<ResultsFilingTime> filings, Domain.Market.Calendar.TradingCalendar? calendar = null) =>
        ResultsEvents.Of(rows, filings, calendar ?? new(), Lag, AsOf).GetValueOrDefault(Who) ?? [];

    [Fact]
    public void A_meeting_without_a_filing_is_the_session_open_of_its_event_session_in_utc()
    {
        var events = Events([Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0))], []);

        var one = events.ShouldHaveSingleItem();
        one.FromCalendar.ShouldBeTrue();
        one.At.ShouldBe(At(8, 12, 9, 15));
        one.At.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_filing_for_the_period_is_the_event_and_the_meeting_of_that_period_adds_nothing()
    {
        var filed = At(8, 12, 16, 12).ToUniversalTime();

        var events = Events([Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0))], [new ResultsFilingTime(Who, Q1, filed)]);

        events.ShouldHaveSingleItem().ShouldBe(new EventInstant(filed, false));
    }

    [Fact]
    public void A_filing_of_another_period_does_not_cover_the_meeting_and_a_filing_without_a_period_covers_nothing()
    {
        var filed = At(5, 12, 16, 12).ToUniversalTime();

        var events = Events([Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0))], [new ResultsFilingTime(Who, new DateOnly(2026, 3, 31), filed), new ResultsFilingTime(Who, null, filed.AddDays(1))]);

        events.Select(e => e.FromCalendar).ShouldBe([false, false, true]);
    }

    [Fact]
    public void A_cancelled_meeting_is_no_event_and_a_rescheduled_one_counts_at_its_new_session_only()
    {
        var first = Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0));
        var moved = Row(2, new DateOnly(2026, 8, 19), At(8, 10, 8, 0), MeetingRow.Rescheduled, supersedes: first.Filing.Id);
        var cancelled = Row(3, new DateOnly(2026, 8, 19), At(8, 11, 8, 0), MeetingRow.Cancelled, supersedes: moved.Filing.Id);

        Events([first, moved], []).ShouldHaveSingleItem().At.ShouldBe(At(8, 19, 9, 15));
        Events([first, moved, cancelled], []).ShouldBeEmpty();
    }

    [Fact]
    public void An_event_session_on_a_saturday_or_a_holiday_is_the_next_session_open()
    {
        Events([Row(1, new DateOnly(2026, 8, 8), At(8, 3, 11, 0))], []).ShouldHaveSingleItem().At.ShouldBe(At(8, 10, 9, 15));
        Events([Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0))], [], WithHoliday(8, 12)).ShouldHaveSingleItem().At.ShouldBe(At(8, 13, 9, 15));
    }

    [Fact]
    public void An_instrument_with_neither_a_filing_nor_a_meeting_is_absent_and_never_an_empty_list()
    {
        var other = Guid.NewGuid();

        var all = ResultsEvents.Of([Row(1, new DateOnly(2026, 8, 12), At(8, 3, 11, 0), instrument: other)], [], new(), Lag, AsOf);

        all.ContainsKey(Who).ShouldBeFalse();
        all.ContainsKey(other).ShouldBeTrue();
    }

    [Fact]
    public void The_events_are_ordered_by_time_and_do_not_depend_on_the_order_of_the_input()
    {
        var late = new ResultsFilingTime(Who, Q1, At(8, 12, 16, 12).ToUniversalTime());
        var early = new ResultsFilingTime(Who, new DateOnly(2026, 3, 31), At(5, 12, 16, 12).ToUniversalTime());

        Events([], [late, early]).Select(e => e.At).ShouldBe([early.BroadcastTs, late.BroadcastTs]);
        Events([], [early, late]).Select(e => e.At).ShouldBe([early.BroadcastTs, late.BroadcastTs]);
    }
}
