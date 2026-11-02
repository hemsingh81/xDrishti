using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>P15-04 criterion 9: the read handler of GET /api/calendar/results.</summary>
public sealed class GetResultsCalendarHandlerTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeCalendarRows _rows = new();

    private readonly FakeInstruments _instruments = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 8, 7, 10, 0, 0, Ist));

    public GetResultsCalendarHandlerTests()
    {
        _instruments.Tracked.AddRange([new TrackedInstrument(FilingWorld.Reliance, "RELIANCE"), new TrackedInstrument(FilingWorld.Tcs, "TCS"), new TrackedInstrument(new Guid("aaaaaaaa-0000-0000-0000-000000000009"), "INFY")]);
    }

    private GetResultsCalendarHandler Handler() => new(new ResultsCalendar(_rows, new FakeTradingCalendar(), new FakeSettings()), _instruments, _clock);

    private static CalendarRow Row(int n, Guid instrument, DateOnly meeting, DateTimeOffset intimated, string status = "scheduled", DateOnly? period = null)
    {
        var p = period ?? new DateOnly(2026, 6, 30);
        var sha = n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
        return new CalendarRow(new StoredFiling(FilingId.Create(instrument, FilingKind.BoardMeeting, p, sha), instrument, FilingKind.BoardMeeting, p, intimated.ToUniversalTime(), BroadcastState.Ok, null, intimated, sha, 1, "m.csv", null), meeting, "results", status);
    }

    private async Task<ResultsCalendarView> GetAsync(string? from = null, string? to = null, string? asOf = null) =>
        (await Handler().HandleAsync(new GetResultsCalendarQuery(from, to, asOf, false), Ct)).Value;

    [Fact]
    public async Task Nothing_stored_is_an_empty_list_and_every_tracked_instrument_is_listed_without_an_entry()
    {
        var view = await GetAsync();

        view.Meetings.ShouldBeEmpty();
        view.Truncated.ShouldBeFalse();
        view.InstrumentsWithoutEntry.Count.ShouldBe(3);
        view.InstrumentsWithoutEntry.Symbols.ShouldBe(["INFY", "RELIANCE", "TCS"]);
        (view.From, view.To).ShouldBe((new DateOnly(2026, 8, 7), new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public async Task Upcoming_meetings_in_the_range_are_listed_with_days_and_the_instruments_with_none_are_counted()
    {
        _rows.Rows.AddRange([Row(1, FilingWorld.Reliance, new DateOnly(2026, 8, 12), new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist)), Row(2, FilingWorld.Tcs, new DateOnly(2026, 8, 8), new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist)), Row(3, FilingWorld.Tcs, new DateOnly(2027, 1, 8), new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist), period: new DateOnly(2026, 12, 31))]);

        var view = await GetAsync();

        view.Meetings.Select(m => (m.Symbol, m.MeetingDate, m.EventSession, m.Days, m.Revision)).ShouldBe([("TCS", new DateOnly(2026, 8, 8), new DateOnly(2026, 8, 10), 1, 1), ("RELIANCE", new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12), 3, 1)]);
        view.Meetings[0].IntimatedAt.ShouldBe(new DateTimeOffset(2026, 8, 3, 11, 0, 0, Ist));
        view.InstrumentsWithoutEntry.Symbols.ShouldBe(["INFY"]);
    }

    [Fact]
    public async Task A_meeting_published_after_asOf_is_not_shown_and_asOf_with_an_offset_moves_the_day_count()
    {
        _rows.Rows.Add(Row(1, FilingWorld.Reliance, new DateOnly(2026, 8, 12), new DateTimeOffset(2026, 8, 6, 11, 0, 0, Ist)));

        var early = await GetAsync(asOf: "2026-08-06T10:00:00+05:30");
        var late = await GetAsync(asOf: "2026-08-10T10:00:00Z");

        early.Meetings.ShouldBeEmpty();
        late.Meetings.ShouldHaveSingleItem().Days.ShouldBe(2);
    }

    [Theory]
    [InlineData("2026-08-01", "2026-12-31", null, "The range is at most 120 days.")]
    [InlineData("2026-08-10", "2026-08-01", null, "from must not be after to.")]
    [InlineData("08/01/2026", null, null, "from and to must be dates such as 2026-08-07.")]
    [InlineData(null, "tomorrow", null, "from and to must be dates such as 2026-08-07.")]
    [InlineData(null, null, "2026-08-07T10:00:00", "asOf must be an instant with an offset, such as 2026-08-07T16:00:00+05:30 or 2026-08-07T10:30:00Z.")]
    [InlineData(null, null, "2026-08-07", "asOf must be an instant with an offset, such as 2026-08-07T16:00:00+05:30 or 2026-08-07T10:30:00Z.")]
    [InlineData(null, null, "1999-12-31T00:00:00Z", "asOf must not be before 2000-01-01.")]
    public async Task A_bad_query_is_calendar_invalid_query(string? from, string? to, string? asOf, string message)
    {
        var result = await Handler().HandleAsync(new GetResultsCalendarQuery(from, to, asOf, false), Ct);

        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Message).ShouldBe(("calendar.invalid_query", message));
    }

    [Fact]
    public async Task An_unknown_parameter_is_calendar_invalid_query()
    {
        var result = await Handler().HandleAsync(new GetResultsCalendarQuery(null, null, null, true), Ct);

        result.Error.Code.ShouldBe("calendar.invalid_query");
    }

    [Fact]
    public async Task The_lists_are_cut_at_their_caps_and_say_so()
    {
        for (var i = 0; i < 350; i++)
        {
            _instruments.Tracked.Add(new TrackedInstrument(Guid.NewGuid(), $"S{i:000}"));
        }

        var view = await GetAsync();

        view.InstrumentsWithoutEntry.Count.ShouldBe(353);
        view.InstrumentsWithoutEntry.Symbols.Count.ShouldBe(300);
    }

    [Fact]
    public async Task A_range_of_exactly_120_days_is_allowed()
    {
        var result = await Handler().HandleAsync(new GetResultsCalendarQuery("2026-08-01", "2026-11-28", null, false), Ct);

        result.IsSuccess.ShouldBeTrue();
    }
}
