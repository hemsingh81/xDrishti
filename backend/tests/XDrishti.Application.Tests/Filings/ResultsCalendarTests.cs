using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Filings;

/// <summary>P15-04 criteria 2, 5, 6, 7: the port over fake rows (lag applied, cutoff passed to the reader, batch form, closed reasons).</summary>
public sealed class ResultsCalendarTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeCalendarRows _rows = new();

    private readonly FakeSettings _settings = new();

    private ResultsCalendar Calendar(TradingCalendar? tc = null) => new(_rows, new FakeTradingCalendar(tc), _settings);

    private static CalendarRow Row(int n, Guid instrument, DateOnly meeting, DateTimeOffset intimated, string status = "scheduled", DateOnly? period = null, Guid? supersedes = null)
    {
        var p = period ?? new DateOnly(2026, 6, 30);
        var sha = n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
        var filing = new StoredFiling(FilingId.Create(instrument, FilingKind.BoardMeeting, p, sha), instrument, FilingKind.BoardMeeting, p, intimated.ToUniversalTime(), BroadcastState.Ok, null, intimated, sha, 1, "m.csv", supersedes);
        return new CalendarRow(filing, meeting, "results", status);
    }

    [Fact]
    public async Task The_reader_gets_the_cutoff_t_minus_the_lag_and_the_answer_follows_the_look_ahead_rule()
    {
        _rows.Rows.Add(Row(1, FilingWorld.Reliance, new DateOnly(2026, 8, 12), new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist)));

        var before = await Calendar().DaysToResults(FilingWorld.Reliance, new DateTimeOffset(2026, 8, 3, 11, 4, 59, Ist), Ct);
        var at = await Calendar().DaysToResults(FilingWorld.Reliance, new DateTimeOffset(2026, 8, 3, 11, 5, 0, Ist), Ct);

        (before.State, before.Reason).ShouldBe(("unavailable", "no_entry"));
        (at.State, at.Days).ShouldBe(("value", 7));
        _rows.Calls[1].Cutoff.ShouldBe(new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist));
    }

    [Fact]
    public async Task A_batch_is_one_query_and_each_instrument_gets_its_own_answer_including_the_one_with_no_entry()
    {
        _rows.Rows.Add(Row(1, FilingWorld.Reliance, new DateOnly(2026, 8, 12), new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist)));

        var answers = await Calendar().DaysToResults([FilingWorld.Reliance, FilingWorld.Tcs, FilingWorld.Tcs], new DateTimeOffset(2026, 8, 7, 10, 0, 0, Ist), Ct);

        _rows.Calls.Count.ShouldBe(1);
        answers.Count.ShouldBe(2);
        answers[FilingWorld.Reliance].Days.ShouldBe(3);
        answers[FilingWorld.Tcs].Reason.ShouldBe("no_entry");
    }

    [Fact]
    public async Task An_empty_batch_asks_nothing()
    {
        (await Calendar().DaysToResults([], DateTimeOffset.UnixEpoch, Ct)).ShouldBeEmpty();

        _rows.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_agenda_lists_every_visible_upcoming_meeting_earliest_first_and_hides_a_reschedule_before_its_time()
    {
        var first = Row(1, FilingWorld.Reliance, new DateOnly(2026, 8, 12), new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist));
        var moved = Row(2, FilingWorld.Reliance, new DateOnly(2026, 8, 20), new DateTimeOffset(2026, 8, 6, 11, 0, 0, Ist), "rescheduled", supersedes: first.Filing.Id);
        _rows.Rows.AddRange([first, moved, Row(3, FilingWorld.Tcs, new DateOnly(2026, 8, 10), new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist))]);

        var before = await Calendar().UpcomingAsync(new DateTimeOffset(2026, 8, 5, 10, 0, 0, Ist), Ct);
        var after = await Calendar().UpcomingAsync(new DateTimeOffset(2026, 8, 7, 10, 0, 0, Ist), Ct);

        before.Select(m => (m.InstrumentId, m.EventSession)).ShouldBe([(FilingWorld.Tcs, new DateOnly(2026, 8, 10)), (FilingWorld.Reliance, new DateOnly(2026, 8, 12))]);
        after.Select(m => (m.InstrumentId, m.EventSession, m.Revision)).ShouldBe([(FilingWorld.Tcs, new DateOnly(2026, 8, 10), 1), (FilingWorld.Reliance, new DateOnly(2026, 8, 20), 2)]);
    }

    [Fact]
    public async Task The_shareholding_port_asks_for_groups_up_to_t_minus_the_lag_and_returns_current_and_previous()
    {
        var rows = new FakeShareholdingRows();
        DateTimeOffset At(int m, int d) => new(2026, m, d, 12, 0, 0, TimeSpan.Zero);
        ShareholdingEnvelope Group(int n, DateOnly period, DateTimeOffset ts) => new(new StoredFiling(Guid.NewGuid(), FilingWorld.Reliance, FilingKind.Shareholding, period, ts, BroadcastState.Ok, null, ts, n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture), 1, "s.csv", null), [new ShareholdingLine("promoter", 50, null)]);
        rows.Groups.AddRange([Group(1, new DateOnly(2026, 3, 31), At(4, 20)), Group(2, new DateOnly(2026, 6, 30), At(7, 20))]);
        var port = new ShareholdingAsOf(rows, _settings);

        var early = await port.Latest(FilingWorld.Reliance, At(7, 20).AddMinutes(4), Ct);
        var late = await port.Latest(FilingWorld.Reliance, At(7, 20).AddMinutes(5), Ct);
        var none = await port.Latest(FilingWorld.Tcs, At(8, 1), Ct);

        (early.Current!.PeriodEnd, early.Previous).ShouldBe((new DateOnly(2026, 3, 31), null));
        (late.Current!.PeriodEnd, late.Previous!.PeriodEnd).ShouldBe((new DateOnly(2026, 6, 30), new DateOnly(2026, 3, 31)));
        (none.State, none.Reason).ShouldBe(("unavailable", "no_entry"));
    }
}
