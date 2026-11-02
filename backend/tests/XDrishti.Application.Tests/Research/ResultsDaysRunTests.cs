#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// P15-05 criterion 7 through the handler: a strategy that names <c>days_to_results</c> or <c>days_since_results</c> gets the results calendar and the results filing times read once before any bar, is refused <c>simulation.filter_not_available</c> with "no results calendar or filings for the
/// window (P15)" when there is neither, and records the hashes, counts, lag and series version in the run header and key; a strategy that names neither reads nothing and its key line set is unchanged.
/// </summary>
public sealed class ResultsDaysRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static ResultsFilingTime Filing(Guid instrument, DateTimeOffset at) => new(instrument, new DateOnly(2025, 12, 31), at.ToUniversalTime());

    private static void Reads(RunFixture fixture, EventCalendarSnapshot snapshot, params ResultsFilingTime[] filings)
    {
        fixture.Engine.ReadsResultsDays = true;
        fixture.EventSnapshots.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(snapshot)));
        fixture.FilingTimes.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ResultsFilingTime>>(filings));
    }

    [Fact]
    public async Task A_strategy_that_names_a_results_days_feature_reads_the_calendar_and_the_filings_once_gives_them_to_every_instrument_and_records_them_in_the_header_and_the_key()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var snapshot = EventCalendarSnapshot.Create([], 5);
        Reads(fixture, snapshot, Filing(abb, new DateTimeOffset(RunFixture.D1.Year, RunFixture.D1.Month, RunFixture.D1.Day, 10, 0, 0, Ist)));

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        await fixture.FilingTimes.Received(1).ReadAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { abb })), Arg.Any<CancellationToken>());
        fixture.Engine.Replays.ShouldAllBe(input => input.ResultsDays != null && input.ResultsDays.FilingCount == 1);
        var record = result.Value.ResultsDays.ShouldNotBeNull();
        (record.CalendarSha256, record.CalendarRows, record.FilingRows, record.LagMinutes, record.RdsVersion).ShouldBe((snapshot.Sha256, 0, 1, 5, "rds-1"));
        record.FilingsSha256.ShouldBe(fixture.Engine.Replays[0].ResultsDays!.FilingsSha256);
        var header = new RunHeader(result.Value.Strategy, result.Value.EngineVersion, result.Value.From, result.Value.To, result.Value.Options, result.Value.AcknowledgeCsvAdjusted, result.Value.Fills, result.Value.Universe, new XDrishti.Domain.Market.Calendar.TradingCalendar(), null, null, null, record);
        RunAssembler.KeyText(header, result.Value.Instruments, result.Value.CalendarExceptions).ShouldContain("\nrdays=" + snapshot.Sha256 + "|" + record.FilingsSha256 + "|5|rds-1\n");
    }

    [Fact]
    public async Task A_strategy_that_reads_a_quintile_gets_the_surprise_numbers_once_and_records_their_hash_in_the_header_and_the_key_and_one_that_does_not_never_asks()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var snapshot = EventCalendarSnapshot.Create([], 5);
        var filing = Filing(abb, new DateTimeOffset(RunFixture.D1.Year, RunFixture.D1.Month, RunFixture.D1.Day, 10, 0, 0, Ist));
        Reads(fixture, snapshot, filing);
        var surprises = SurpriseInputs.Create([new SurpriseObservation(abb, new DateOnly(2025, 12, 31), filing.BroadcastTs, 1.5, 0.02, Unverified: true)], TimeSpan.FromMinutes(5), new XDrishti.Domain.Market.Calendar.TradingCalendar());
        fixture.SurpriseReader.ReadAsync(Arg.Any<IReadOnlyList<ResultsFilingTime>>(), Arg.Any<EventCalendarSnapshot>(), Arg.Any<XDrishti.Domain.Market.Calendar.TradingCalendar>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(surprises));

        var plain = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);
        await fixture.SurpriseReader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default!, default!, default, default);
        plain.Value.ResultsDays!.SurpriseSha256.ShouldBeNull("a strategy that names no quintile leaves its key line as P15-05 wrote it");

        fixture.Engine.ReadsSurprise = true;
        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        await fixture.SurpriseReader.Received(1).ReadAsync(Arg.Is<IReadOnlyList<ResultsFilingTime>>(f => f.Count == 1), snapshot, Arg.Any<XDrishti.Domain.Market.Calendar.TradingCalendar>(), TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
        var record = result.Value.ResultsDays.ShouldNotBeNull();
        (record.SurpriseSha256, record.SurpriseRows, record.SbpVersion, record.SurpriseUnverified).ShouldBe((surprises.Sha256, 1, "sbp-1", 1));
        fixture.Engine.Replays.Last().ResultsDays!.Surprises.ShouldBeSameAs(surprises);
        var header = new RunHeader(result.Value.Strategy, result.Value.EngineVersion, result.Value.From, result.Value.To, result.Value.Options, result.Value.AcknowledgeCsvAdjusted, result.Value.Fills, result.Value.Universe, new XDrishti.Domain.Market.Calendar.TradingCalendar(), null, null, null, record);
        RunAssembler.KeyText(header, result.Value.Instruments, result.Value.CalendarExceptions).ShouldContain("|rds-1|" + surprises.Sha256 + "|sbp-1\n");
    }

    [Fact]
    public async Task A_window_with_neither_a_calendar_row_nor_a_filing_is_refused_before_any_bar_is_read()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        Reads(fixture, EventCalendarSnapshot.Create([], 5));

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SimulationErrors.StrategyInvalidCode);
        var field = result.Error.Fields!.ShouldHaveSingleItem();
        (field.Field, field.Code, field.Message).ShouldBe(("filters", "simulation.filter_not_available", "no results calendar or filings for the window (P15)"));
        fixture.Store.Reads.ShouldBeEmpty();
        fixture.Engine.Replays.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_filing_broadcast_after_the_end_of_the_window_is_not_given_to_the_run_and_does_not_change_its_key()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var inside = Filing(abb, new DateTimeOffset(RunFixture.D1.Year, RunFixture.D1.Month, RunFixture.D1.Day, 10, 0, 0, Ist));
        var outside = Filing(abb, new DateTimeOffset(RunFixture.D3.Year, RunFixture.D3.Month, RunFixture.D3.Day, 10, 0, 0, Ist).AddDays(1));
        Reads(fixture, EventCalendarSnapshot.Create([], 5), inside, outside);

        var withLater = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);
        var again = new RunFixture();
        again.Add("NSE:ABB", RunFixture.ThreeDays(), id: abb);
        Reads(again, EventCalendarSnapshot.Create([], 5), inside);
        var without = await again.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        withLater.Value.ResultsDays!.FilingRows.ShouldBe(1);
        withLater.Value.RunKey.ShouldBe(without.Value.RunKey);
    }

    [Fact]
    public async Task A_strategy_that_names_neither_feature_reads_no_filing_and_carries_no_record()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        fixture.FilingTimes.ReceivedCalls().ShouldBeEmpty();
        fixture.EventSnapshots.ReceivedCalls().ShouldBeEmpty();
        fixture.Engine.Replays.ShouldAllBe(input => input.ResultsDays == null);
        result.Value.ResultsDays.ShouldBeNull();
    }
}
