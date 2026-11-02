using XDrishti.Domain.Filings;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-06 criterion 4: the snapshot is immutable, canonical and hashed so that the run key can tell one calendar from another; the lag is part of what a run read.</summary>
public sealed class EventCalendarSnapshotTests
{
    private static readonly DateTimeOffset Intimated = At(8, 3, 11, 0);

    [Fact]
    public void The_hash_does_not_depend_on_the_order_of_the_rows_or_on_a_row_given_twice()
    {
        var a = Row(1, new DateOnly(2026, 8, 12), Intimated);
        var b = Row(2, new DateOnly(2026, 11, 10), At(10, 20, 10, 0), period: new DateOnly(2026, 9, 30));

        var one = EventCalendarSnapshot.Create([a, b], 5);
        var two = EventCalendarSnapshot.Create([b, a, a], 5);

        two.Sha256.ShouldBe(one.Sha256);
        (two.RowCount, one.RowCount).ShouldBe((2, 2));
    }

    [Fact]
    public void One_changed_field_of_one_row_changes_the_hash()
    {
        var baseline = EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 12), Intimated)], 5).Sha256;

        EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 13), Intimated)], 5).Sha256.ShouldNotBe(baseline, "meeting date");
        EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 12), Intimated.AddMinutes(1))], 5).Sha256.ShouldNotBe(baseline, "intimation");
        EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 12), Intimated, MeetingRow.Rescheduled)], 5).Sha256.ShouldNotBe(baseline, "status");
        EventCalendarSnapshot.Create([Row(2, new DateOnly(2026, 8, 12), Intimated)], 5).Sha256.ShouldNotBe(baseline, "content hash");
        EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 12), Intimated, instrument: Guid.NewGuid())], 5).Sha256.ShouldNotBe(baseline, "instrument");
    }

    [Fact]
    public void A_time_finer_than_a_microsecond_is_cut_to_the_store_before_it_is_hashed_and_compared()
    {
        var fine = Intimated.AddTicks(7);   // 700 ns past the microsecond
        var row = Row(1, new DateOnly(2026, 8, 12), fine);

        var snapshot = EventCalendarSnapshot.Create([row], 5);

        snapshot.Sha256.ShouldBe(EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 12), Intimated)], 5).Sha256);
        snapshot.RowsOf(row.Filing.InstrumentId).Single().Filing.BroadcastTs!.Value.UtcTicks.ShouldBe(Intimated.UtcTicks);
    }

    [Fact]
    public void Rows_that_can_never_be_visible_or_are_not_results_meetings_are_left_out()
    {
        var undated = Row(1, new DateOnly(2026, 8, 12), null);
        var other = Row(2, new DateOnly(2026, 8, 12), Intimated, purpose: MeetingRow.Other);
        var good = Row(3, new DateOnly(2026, 8, 12), Intimated);

        var snapshot = EventCalendarSnapshot.Create([undated, other, good], 5);

        snapshot.RowCount.ShouldBe(1);
        snapshot.InstrumentCount.ShouldBe(1);
        EventCalendarSnapshot.Create([undated, other], 5).RowCount.ShouldBe(0);
    }

    [Fact]
    public void The_lag_is_part_of_the_answer_and_a_lag_out_of_the_store_range_is_refused()
    {
        var snapshot0 = EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 12), Intimated)], 0);
        var snapshot60 = EventCalendarSnapshot.Create([Row(1, new DateOnly(2026, 8, 12), Intimated)], 60);
        var t = Intimated.AddMinutes(30);

        snapshot0.Answer(FilingSamples.Instrument, t, new()).State.ShouldBe("value");
        snapshot60.Answer(FilingSamples.Instrument, t, new()).State.ShouldBe("unavailable");
        snapshot0.LagMinutes.ShouldBe(0);
        snapshot0.Sha256.ShouldBe(snapshot60.Sha256, "the rows are the same; the lag travels beside the hash in the run key");
        Should.Throw<ArgumentOutOfRangeException>(() => EventCalendarSnapshot.Create([], -1));
        Should.Throw<ArgumentOutOfRangeException>(() => EventCalendarSnapshot.Create([], 61));
    }

    [Fact]
    public void An_empty_snapshot_has_no_rows_and_answers_no_entry()
    {
        var empty = EventCalendarSnapshot.Create([], 5);

        (empty.RowCount, empty.InstrumentCount).ShouldBe((0, 0));
        empty.Answer(FilingSamples.Instrument, Intimated, new()).Reason.ShouldBe(ResultsAnswer.NoEntry);
    }
}
