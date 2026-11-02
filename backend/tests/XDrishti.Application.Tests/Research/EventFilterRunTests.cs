#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// P15-06 criterion 2 through the handler: a strategy that names <c>not_event_day</c> gets the results calendar read once before any bar, is refused <c>simulation.filter_not_available</c> with "no results calendar rows for the window (P15-04)" when the window has no row (and when the read fails),
/// and records the snapshot hash, rows, lag and rule version in the run header and key; a strategy that does not name it reads no calendar and its key line set is unchanged.
/// </summary>
public sealed class EventFilterRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EventCalendarSnapshot Snapshot(Guid instrument) => EventCalendarSnapshot.Create([CalendarRowFor(instrument)], 5);

    private static CalendarRow CalendarRowFor(Guid instrument)
    {
        var sha = new string('1', 64);
        var filing = new StoredFiling(FilingId.Create(instrument, FilingKind.BoardMeeting, new DateOnly(2025, 12, 31), sha), instrument, FilingKind.BoardMeeting, new DateOnly(2025, 12, 31), new DateTimeOffset(2026, 2, 20, 5, 30, 0, TimeSpan.Zero), BroadcastState.Ok, null, DateTimeOffset.UnixEpoch, sha, 10, "m.csv", null);
        return new CalendarRow(filing, RunFixture.D2, MeetingRow.Results, MeetingRow.Scheduled);
    }

    [Fact]
    public async Task A_strategy_that_names_the_filter_reads_the_snapshot_once_gives_it_to_every_instrument_and_records_it_in_the_header_and_the_key()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var snapshot = Snapshot(abb);
        fixture.Engine.ReadsEvents = true;
        fixture.EventSnapshots.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(snapshot)));

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        await fixture.EventSnapshots.Received(1).ReadAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { abb })), RunFixture.D3, Arg.Any<CancellationToken>());
        fixture.Engine.Replays.ShouldAllBe(input => ReferenceEquals(input.Events, snapshot));
        result.Value.EventFilter.ShouldBe(new EventFilterRecord(snapshot.Sha256, 1, 5, "evf-1"));
        var header = new RunHeader(result.Value.Strategy, result.Value.EngineVersion, result.Value.From, result.Value.To, result.Value.Options, result.Value.AcknowledgeCsvAdjusted, result.Value.Fills, result.Value.Universe, new Domain.Market.Calendar.TradingCalendar(), Events: result.Value.EventFilter);
        RunAssembler.KeyText(header, result.Value.Instruments, result.Value.CalendarExceptions).ShouldContain("\nevents=" + snapshot.Sha256 + "|5|evf-1\n");
    }

    [Fact]
    public async Task A_window_with_no_calendar_row_at_all_is_refused_before_any_bar_is_read()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        fixture.Engine.ReadsEvents = true;
        fixture.EventSnapshots.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(EventCalendarSnapshot.Create([], 5))));

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SimulationErrors.StrategyInvalidCode);
        var field = result.Error.Fields!.ShouldHaveSingleItem();
        (field.Field, field.Code, field.Message).ShouldBe(("filters", "simulation.filter_not_available", "no results calendar rows for the window (P15-04)"));
        fixture.Store.Reads.ShouldBeEmpty();
        fixture.Engine.Replays.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_calendar_that_cannot_be_read_refuses_the_run_the_same_way_and_never_runs_over_an_empty_calendar()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        fixture.Engine.ReadsEvents = true;
        fixture.EventSnapshots.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<EventCalendarSnapshot>(Error.Validation(SimulationErrors.FilterNotAvailableCode, "The results calendar could not be read (P15-04)."))));

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.Error.Fields!.Single().Code.ShouldBe("simulation.filter_not_available");
        fixture.Store.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_strategy_that_does_not_name_the_filter_reads_no_calendar_and_carries_no_event_record()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        fixture.EventSnapshots.ReceivedCalls().ShouldBeEmpty();
        fixture.Engine.Replays.ShouldAllBe(input => input.Events == null);
        result.Value.EventFilter.ShouldBeNull();
    }

    [Fact]
    public async Task The_snapshot_service_reads_once_with_the_cutoff_at_the_end_of_the_window_minus_the_lag_and_builds_the_rows_the_run_will_use()
    {
        var instrument = Guid.NewGuid();
        var rows = Substitute.For<ICalendarRows>();
        rows.ResultsRowsAsync(Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<CalendarRow>>([CalendarRowFor(instrument)]));
        var settings = Substitute.For<IFilingSettings>();
        settings.LagMinutes.Returns(5);
        var service = new EventCalendarSnapshots(rows, settings, NullLogger<EventCalendarSnapshots>.Instance);

        var snapshot = await service.ReadAsync([instrument, instrument], new DateOnly(2026, 3, 4), Ct);

        snapshot.Value.RowCount.ShouldBe(1);
        snapshot.Value.LagMinutes.ShouldBe(5);
        var end = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.FromMinutes(330)).AddTicks(-10);
        await rows.Received(1).ResultsRowsAsync(Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids!.Count == 1), end - TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_snapshot_service_turns_a_failing_read_into_a_failure_and_lets_a_cancellation_through()
    {
        var rows = Substitute.For<ICalendarRows>();
        var settings = Substitute.For<IFilingSettings>();
        settings.LagMinutes.Returns(5);
        var service = new EventCalendarSnapshots(rows, settings, NullLogger<EventCalendarSnapshots>.Instance);
        rows.ResultsRowsAsync(Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns<ValueTask<IReadOnlyList<CalendarRow>>>(_ => throw new InvalidOperationException("the store is away"));

        var failed = await service.ReadAsync([Guid.NewGuid()], new DateOnly(2026, 3, 4), Ct);
        rows.ResultsRowsAsync(Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns<ValueTask<IReadOnlyList<CalendarRow>>>(_ => throw new OperationCanceledException());

        failed.Error.Code.ShouldBe("simulation.filter_not_available");
        failed.Error.Message.ShouldNotContain("away", Case.Insensitive);
        await Should.ThrowAsync<OperationCanceledException>(async () => await service.ReadAsync([Guid.NewGuid()], new DateOnly(2026, 3, 4), Ct));
    }

    [Fact]
    public async Task An_empty_instrument_list_reads_nothing_and_gives_an_empty_snapshot()
    {
        var rows = Substitute.For<ICalendarRows>();
        var settings = Substitute.For<IFilingSettings>();
        settings.LagMinutes.Returns(5);

        var snapshot = await new EventCalendarSnapshots(rows, settings, NullLogger<EventCalendarSnapshots>.Instance).ReadAsync([], new DateOnly(2026, 3, 4), Ct);

        snapshot.Value.RowCount.ShouldBe(0);
        rows.ReceivedCalls().ShouldBeEmpty();
    }
}
