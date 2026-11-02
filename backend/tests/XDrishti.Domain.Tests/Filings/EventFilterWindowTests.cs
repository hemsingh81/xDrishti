using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>
/// P15-06 criteria 1, 2 and 5: the window rule on the hand table of the spec (results event session Wednesday 2026-08-12; Friday 2026-08-07 is a session), the strategy filter <c>not_event_day</c> with the hold of the side as its window, the selection window
/// <c>exclude.results_within_days</c>, the boundary days, a holiday and a Saturday event session (through P15-04), and <c>unavailable</c> never a pass in any consumer.
/// </summary>
public sealed class EventFilterWindowTests
{
    private static readonly Guid Who = FilingSamples.Instrument;

    private static readonly DateTimeOffset Intimated = At(8, 3, 11, 0);

    private static readonly TradingCalendar Plain = new();

    private static EventCalendarSnapshot Snapshot(DateOnly meeting, DateTimeOffset? intimated = null) =>
        EventCalendarSnapshot.Create([Row(1, meeting, intimated ?? Intimated)], 5);

    private static readonly EventCalendarSnapshot Wednesday = Snapshot(new DateOnly(2026, 8, 12));

    /// <summary>The table of the spec: decision date, days_to_results, exclude N=2, not_event_day with H=3, not_event_day with H=0, the plan check (limit 3).</summary>
    public static TheoryData<int, int, int, bool, WindowOutcome, WindowOutcome, bool> Table => new()
    {
        { 8, 7, 3, false, WindowOutcome.Inside, WindowOutcome.Outside, false },
        { 8, 10, 2, true, WindowOutcome.Inside, WindowOutcome.Outside, false },
        { 8, 11, 1, true, WindowOutcome.Inside, WindowOutcome.Outside, false },
        { 8, 12, 0, true, WindowOutcome.Inside, WindowOutcome.Inside, false },
        { 8, 6, 4, false, WindowOutcome.Outside, WindowOutcome.Outside, true },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void The_hand_table_of_the_spec_holds_for_each_consumer(int month, int day, int days, bool excluded, WindowOutcome hold3, WindowOutcome intraday, bool checkPasses)
    {
        var t = At(month, day, 10, 30);
        var answer = Wednesday.Answer(Who, t, Plain);

        answer.Days.ShouldBe(days);
        ResultsWindow.ExcludedByEvent(answer, ResultsWindow.DefaultDays).ShouldBe(excluded);
        EventDayFilter.Evaluate(Wednesday, Who, t, Plain, 3).ShouldBe(hold3);
        EventDayFilter.Evaluate(Wednesday, Who, t, Plain, 0).ShouldBe(intraday);
        (ResultsWindow.DaysOrNull(answer) > 3).ShouldBe(checkPasses);
    }

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(2, 2, true)]
    [InlineData(2, 3, false)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(10, 10, true)]
    [InlineData(10, 11, false)]
    public void The_window_is_inclusive_at_both_ends_for_every_n(int n, int days, bool inside)
    {
        var answer = new ResultsAnswer(ResultsAnswer.Value, days, null, null, null, null);

        ResultsWindow.Classify(answer, n).ShouldBe(inside ? WindowOutcome.Inside : WindowOutcome.Outside);
        ResultsWindow.ExcludedByEvent(answer, n).ShouldBe(inside);
    }

    [Fact]
    public void Unavailable_is_unavailable_in_every_consumer_and_never_a_pass()
    {
        var none = ResultsAnswer.Missing(ResultsAnswer.NoEntry);
        var negative = new ResultsAnswer(ResultsAnswer.Value, -1, null, null, null, null);
        var noNumber = new ResultsAnswer(ResultsAnswer.Value, null, null, null, null, null);

        foreach (var answer in new[] { none, negative, noNumber })
        {
            ResultsWindow.Classify(answer, 2).ShouldBe(WindowOutcome.Unavailable);
            ResultsWindow.ExcludedByEvent(answer, 2).ShouldBeNull();
            ResultsWindow.DaysOrNull(answer).ShouldBeNull();
        }

        var empty = EventCalendarSnapshot.Create([], 5);
        EventDayFilter.Evaluate(empty, Who, At(8, 10, 10), Plain, 3).ShouldBe(WindowOutcome.Unavailable);
        EventDayFilter.Evaluate(empty, Who, At(8, 10, 10), Plain, 0).ShouldBe(WindowOutcome.Unavailable);
    }

    [Fact]
    public void An_instrument_with_other_rows_only_is_unavailable_for_the_filter()
    {
        var other = Guid.NewGuid();
        var snapshot = EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 12), Intimated, instrument: other)], 5);

        EventDayFilter.Evaluate(snapshot, Who, At(8, 11, 10), Plain, 3).ShouldBe(WindowOutcome.Unavailable);
        EventDayFilter.Evaluate(snapshot, other, At(8, 11, 10), Plain, 3).ShouldBe(WindowOutcome.Inside);
    }

    [Fact]
    public void A_meeting_intimated_after_the_decision_does_not_exist_at_it_and_exists_from_intimation_plus_lag_on()
    {
        var snapshot = Snapshot(new DateOnly(2026, 8, 12), At(8, 11, 9, 0));

        EventDayFilter.Evaluate(snapshot, Who, At(8, 11, 9, 4, 59), Plain, 3).ShouldBe(WindowOutcome.Unavailable);
        EventDayFilter.Evaluate(snapshot, Who, At(8, 11, 9, 5, 0), Plain, 3).ShouldBe(WindowOutcome.Inside);
        EventDayFilter.Evaluate(snapshot, Who, At(8, 10, 15, 0), Plain, 3).ShouldBe(WindowOutcome.Unavailable);
    }

    [Fact]
    public void A_cancelled_or_rescheduled_meeting_changes_the_answer_only_from_its_own_visible_time()
    {
        var first = Row(1, new DateOnly(2026, 8, 12), Intimated);
        var moved = Row(2, new DateOnly(2026, 8, 19), At(8, 10, 8, 0), MeetingRow.Rescheduled, supersedes: first.Filing.Id);
        var snapshot = EventCalendarSnapshot.Create([first, moved], 5);

        EventDayFilter.Evaluate(snapshot, Who, At(8, 10, 7, 59), Plain, 3).ShouldBe(WindowOutcome.Inside);   // the reschedule does not exist yet
        EventDayFilter.Evaluate(snapshot, Who, At(8, 10, 8, 5), Plain, 3).ShouldBe(WindowOutcome.Outside);    // from its own time the meeting is a week later
        var cancelled = EventCalendarSnapshot.Create([first, Row(3, new DateOnly(2026, 8, 12), At(8, 10, 8, 0), MeetingRow.Cancelled, supersedes: first.Filing.Id)], 5);
        EventDayFilter.Evaluate(cancelled, Who, At(8, 11, 10, 0), Plain, 3).ShouldBe(WindowOutcome.Unavailable);
    }

    [Fact]
    public void A_saturday_meeting_has_its_event_session_on_monday_and_a_holiday_event_moves_to_the_next_session()
    {
        var saturday = Snapshot(new DateOnly(2026, 8, 8));
        var holiday = Snapshot(new DateOnly(2026, 8, 12));

        saturday.Answer(Who, At(8, 7, 10), Plain).Days.ShouldBe(1);
        EventDayFilter.Evaluate(saturday, Who, At(8, 7, 10), Plain, 0).ShouldBe(WindowOutcome.Outside);
        EventDayFilter.Evaluate(saturday, Who, At(8, 7, 10), Plain, 1).ShouldBe(WindowOutcome.Inside);
        holiday.Answer(Who, At(8, 7, 10), WithHoliday(8, 12)).Days.ShouldBe(3);
        EventDayFilter.Evaluate(holiday, Who, At(8, 12, 10), WithHoliday(8, 12), 0).ShouldBe(WindowOutcome.Outside, "the 12th is closed: the event session is the 13th, one session away");
    }

    [Fact]
    public void The_hold_is_the_window_and_a_longer_hold_blocks_what_a_shorter_one_takes()
    {
        var t = At(8, 6, 10, 0);   // four sessions before the event

        EventDayFilter.Evaluate(Wednesday, Who, t, Plain, 3).ShouldBe(WindowOutcome.Outside);
        EventDayFilter.Evaluate(Wednesday, Who, t, Plain, 4).ShouldBe(WindowOutcome.Inside);
        Should.Throw<ArgumentOutOfRangeException>(() => EventDayFilter.Evaluate(Wednesday, Who, t, Plain, -1));
        Should.Throw<ArgumentOutOfRangeException>(() => ResultsWindow.Classify(ResultsAnswer.Missing(ResultsAnswer.NoEntry), -1));
    }
}
