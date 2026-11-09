using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Repair;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>The owner's repair of old bar holes (ISS-29, ADR 0233): off by default, bounded, fills only the missing minutes, never edits a stored bar.</summary>
public sealed class RepairBarHolesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Wed = new(2026, 10, 7);
    private static readonly DateOnly Thu = new(2026, 10, 8);

    private static readonly string[] AggregateAndQuality = ["aggregate", "quality"];

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly IBarQueries _queries = Substitute.For<IBarQueries>();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();
    private readonly ICommandHandler<StartDataJobCommand, DataJobView> _start = Substitute.For<ICommandHandler<StartDataJobCommand, DataJobView>>();
    private readonly FakeIntradaySource _source = new();
    private readonly FakeAccountStatus _account = FakeAccountStatus.Connected();
    private readonly FakeFetchTargets _targets = new();
    private readonly FakeBarStore _store = new();
    private readonly FakeDataJobStore _jobs = new();
    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();
    private readonly IMarketUnitOfWork _uow = Substitute.For<IMarketUnitOfWork>();
    private readonly FakeDataSettings _settings = new() { RepairMaxSessions = 5 };
    private readonly Guid _id = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RepairBarHolesTests()
    {
        _queries.FindInstrumentAsync(_id, Arg.Any<CancellationToken>()).Returns(new BarsInstrument(_id, "NSE:ABB", "ABB Ltd"));
        _calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        _targets.Targets.Add(new FetchTarget(_id, "NSE:ABB", "share", "1", "NSE_EQ", null));
        _start.HandleAsync(Arg.Any<StartDataJobCommand>(), Arg.Any<CancellationToken>()).Returns(ci => Result.Failure<DataJobView>(DataErrors.JobRunning));
    }

    private static long Minute(DateOnly day, int minuteOfDay) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).AddMinutes(minuteOfDay).ToUnixTimeSeconds();

    private static Bar BarAt(long epoch, double price) => new(epoch, price, price + 1, price - 1, price, 100);

    /// <summary>Stores every minute of the two days except <paramref name="holes"/> minutes of the day (from the open).</summary>
    private void Store(params int[] holes)
    {
        var bars = new List<Bar>();
        foreach (var day in new[] { Wed, Thu })
        {
            for (var m = SessionClock.OpenMinute; m < SessionClock.CloseMinute; m++)
            {
                if (day == Wed && holes.Contains(m))
                {
                    continue;
                }

                bars.Add(BarAt(Minute(day, m), 100));
            }
        }

        _store.Stored[_id] = bars;
    }

    private RepairBarHolesHandler Handler() => new(_queries, _source, _account, _targets, _store, _settings, _calendar, _jobs, _start, _events, _uow, _clock, NullLogger<RepairBarHolesHandler>.Instance);

    private async Task<Result<RepairBarHolesResult>> RunAsync(DateOnly from, DateOnly to) =>
        await Handler().HandleAsync(new RepairBarHolesCommand(_id, from, to, "owner@example.invalid"), Ct);

    [Fact]
    public async Task It_is_off_by_default_and_asks_nobody()
    {
        _settings.RepairMaxSessions = 0;

        var result = await RunAsync(Wed, Thu);

        (result.Error.Code, result.Error.Kind).ShouldBe(("data.repair_disabled", ErrorKind.Conflict));
        _source.Plans.ShouldBeEmpty();
        _store.Reads.ShouldBeEmpty();
        _jobs.Started.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2026-10-08", "2026-10-07", "from")] // reversed
    [InlineData("2026-10-07", "2026-10-12", "to")] // the open session of today is not closed
    [InlineData("2026-10-03", "2026-10-04", "from")] // no session in it (a weekend)
    [InlineData("2026-10-01", "2026-10-09", "to")] // seven sessions, the setting allows five
    [InlineData("2025-01-01", "2026-10-09", "to")] // longer than the span a typed range may have
    public async Task A_window_that_is_reversed_open_empty_or_longer_than_the_setting_is_invalid_names_the_field_and_reads_and_asks_nobody(string from, string to, string field)
    {
        var result = await RunAsync(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldBe([field]);
        _source.Plans.ShouldBeEmpty();
        _store.Reads.ShouldBeEmpty();
        _jobs.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_window_without_holes_fetches_nothing()
    {
        Store();

        var result = await RunAsync(Wed, Thu);

        result.Value.ShouldBe(new RepairBarHolesResult("NSE:ABB", Wed, Thu, 2, 0, 0, 0, 0, 0, null));
        _source.Plans.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_stores_only_the_missing_minutes_asks_only_for_the_days_with_holes_and_never_changes_a_stored_bar()
    {
        int[] holes = [10 * 60 + 52, 10 * 60 + 53, 10 * 60 + 54];
        Store(holes);
        _source.Respond = t => new IntradayResult(
            t.InstrumentId,
            [.. holes.Select(m => BarAt(Minute(Wed, m), 250)), BarAt(Minute(Wed, 10 * 60), 999), BarAt(Minute(Thu, 10 * 60), 999)],
            1);
        _start.HandleAsync(Arg.Any<StartDataJobCommand>(), Arg.Any<CancellationToken>()).Returns(ci => Result.Failure<DataJobView>(DataErrors.JobRunning));

        var result = await RunAsync(Wed, Thu);

        var view = result.Value;
        (view.SessionsChecked, view.SessionsWithHoles, view.MissingBefore, view.Fetched, view.Inserted, view.MissingAfter).ShouldBe((2, 1, 3, 3, 3L, 0));
        view.AggregateJobId.ShouldBeNull("a running job is not an error of the repair");
        var window = _source.Plans.Single().Targets.Single().Windows;
        window.Min(w => w.From).ShouldBe(new DateTimeOffset(Wed.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset));
        window.Max(w => w.To).ShouldBe(new DateTimeOffset(Wed.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset));
        _store.Reads.Single().ShouldSatisfyAllConditions(r => r.From.Offset.ShouldBe(TimeSpan.Zero), r => r.To.Offset.ShouldBe(TimeSpan.Zero)); // the store takes UTC instants only
        var merged = _store.Sessions.Single();
        merged.Source.ShouldBe(BarSource.Dhan);
        merged.Bars.Select(b => b.EpochSeconds).Order().ShouldBe(holes.Select(m => Minute(Wed, m)).Order());
    }

    [Fact]
    public async Task What_the_broker_does_not_give_stays_missing_and_after_a_fill_the_aggregates_are_rebuilt()
    {
        Store(10 * 60, 10 * 60 + 1);
        _source.Respond = t => new IntradayResult(t.InstrumentId, [BarAt(Minute(Wed, 10 * 60), 250)], 1);
        var real = new StartDataJobHandler(new FakeDataJobStore(), Substitute.For<IDataJobChannel>(), Substitute.For<IInstrumentCatalog>(), new FakeCsvFolder(), _clock, NullLogger<StartDataJobHandler>.Instance);
        var job = (await real.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate"]), "owner"), Ct)).Value;
        var jobId = job.Id;
        _start.HandleAsync(Arg.Any<StartDataJobCommand>(), Arg.Any<CancellationToken>()).Returns(ci => Result.Success(job));

        var result = await RunAsync(Wed, Wed);

        (result.Value.Inserted, result.Value.MissingAfter, result.Value.AggregateJobId).ShouldBe((1L, 1, jobId));
        await _start.Received(1).HandleAsync(
            Arg.Is<StartDataJobCommand>(c => c.Request.Steps!.SequenceEqual(AggregateAndQuality) && c.InstrumentId == _id && c.Request.RefetchGaps != true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_fetch_is_a_conflict_and_stores_nothing()
    {
        Store(10 * 60);
        _source.Respond = t => new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Failed, "Dhan said no"));

        var result = await RunAsync(Wed, Wed);

        (result.Error.Code, result.Error.Message).ShouldBe(("data.repair_fetch_failed", "Dhan said no"));
        _store.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_token_an_unavailable_source_and_an_untracked_instrument_stop_it_before_any_read()
    {
        Store(10 * 60);
        _targets.Targets.Clear();
        (await RunAsync(Wed, Wed)).Error.Code.ShouldBe("data.dhan_not_available");

        _targets.Targets.Add(new FetchTarget(_id, "NSE:ABB", "share", "1", "NSE_EQ", null));
        _start.HandleAsync(Arg.Any<StartDataJobCommand>(), Arg.Any<CancellationToken>()).Returns(ci => Result.Failure<DataJobView>(DataErrors.JobRunning));
        _source.Respond = t => new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Unauthorized, "401"));
        (await RunAsync(Wed, Wed)).Error.Code.ShouldBe("data.dhan_not_connected");

        _source.Availability = new SourceAvailability(false, "off");
        (await RunAsync(Wed, Wed)).Error.Code.ShouldBe("data.dhan_not_available");

        (await Handler().HandleAsync(new RepairBarHolesCommand(Guid.NewGuid(), Wed, Wed, "o"), Ct)).Error.Code.ShouldBe("data.instrument_not_found");
    }

    private static readonly int[] ThreeHoles = [10 * 60 + 52, 10 * 60 + 53, 10 * 60 + 54];

    private void ServeHoles() => _source.Respond = t => new IntradayResult(t.InstrumentId, [.. ThreeHoles.Select(m => BarAt(Minute(Wed, m), 250))], 1);

    [Fact]
    public async Task The_whole_repair_holds_the_job_slot_and_releases_it_at_the_end()
    {
        Store(ThreeHoles);
        var runningDuringFetch = false;
        _source.Respond = t =>
        {
            runningDuringFetch = _jobs.Jobs.Values.Count(j => j.IsRunning) == 1;
            return new IntradayResult(t.InstrumentId, [.. ThreeHoles.Select(m => BarAt(Minute(Wed, m), 250))], 1);
        };

        (await RunAsync(Wed, Thu)).IsSuccess.ShouldBeTrue();

        runningDuringFetch.ShouldBeTrue();
        _jobs.Jobs.Values.ShouldAllBe(j => !j.IsRunning);
        _jobs.Jobs.Values.Single().Status.ShouldBe(DataJobStatus.Succeeded);
    }

    [Fact]
    public async Task While_another_job_runs_the_repair_is_refused_before_it_fetches_or_writes()
    {
        Store(ThreeHoles);
        var real = new StartDataJobHandler(_jobs, Substitute.For<IDataJobChannel>(), Substitute.For<IInstrumentCatalog>(), new FakeCsvFolder(), _clock, NullLogger<StartDataJobHandler>.Instance);
        (await real.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate"]), "owner"), Ct)).IsSuccess.ShouldBeTrue();

        var result = await RunAsync(Wed, Thu);

        (result.Error.Code, result.Error.Kind).ShouldBe(("data.job_running", ErrorKind.Conflict));
        _source.Plans.ShouldBeEmpty();
        _store.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_double_submit_is_refused_while_the_first_is_in_flight_and_inserts_once()
    {
        Store(ThreeHoles);
        Result<RepairBarHolesResult>? second = null;
        _source.Respond = t =>
        {
            second = RunAsync(Wed, Thu).GetAwaiter().GetResult();
            return new IntradayResult(t.InstrumentId, [.. ThreeHoles.Select(m => BarAt(Minute(Wed, m), 250))], 1);
        };

        var first = await RunAsync(Wed, Thu);

        second!.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("data.job_running");
        first.Value.Inserted.ShouldBe(3L);
        _store.Sessions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_bar_that_appears_between_the_read_and_the_merge_is_never_overwritten()
    {
        Store(ThreeHoles);
        ServeHoles();
        _store.AppearBeforeMerge = [BarAt(Minute(Wed, ThreeHoles[0]), 777)];

        var result = await RunAsync(Wed, Thu);

        result.Value.Inserted.ShouldBe(2L);
        result.Value.MissingAfter.ShouldBe(1);
        _store.Sessions.Single().InsertOnly.ShouldBeTrue();
        _store.Stored[_id].Single(b => b.EpochSeconds == Minute(Wed, ThreeHoles[0])).Close.ShouldBe(777);
    }

    [Fact]
    public async Task Each_repair_is_audited_with_actor_instrument_window_and_counts()
    {
        Store(ThreeHoles);
        ServeHoles();

        await RunAsync(Wed, Thu);

        _events.Received(1).Record(Arg.Is<AccountEvent>(e =>
            e.Event == AccountEventType.BarHolesRepaired && e.Actor == "owner@example.invalid" && e.Detail == "instrument=NSE:ABB window=2026-10-07..2026-10-08 missing=3 inserted=3"));
        await _uow.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_follow_up_job_that_cannot_start_for_another_reason_than_a_running_job_says_why()
    {
        Store(ThreeHoles);
        ServeHoles();
        _start.HandleAsync(Arg.Any<StartDataJobCommand>(), Arg.Any<CancellationToken>()).Returns(ci => Result.Failure<DataJobView>(DataErrors.Invalid("steps", "No.")));

        var result = await RunAsync(Wed, Thu);

        result.Value.AggregateJobId.ShouldBeNull();
        result.Value.AggregateNote!.ShouldStartWith("The aggregates were not rebuilt");
    }

    [Fact]
    public async Task A_running_job_at_the_follow_up_is_told_to_the_owner_as_a_note()
    {
        Store(ThreeHoles);
        ServeHoles();
        _start.HandleAsync(Arg.Any<StartDataJobCommand>(), Arg.Any<CancellationToken>()).Returns(ci => Result.Failure<DataJobView>(DataErrors.JobRunning));

        (await RunAsync(Wed, Thu)).Value.AggregateNote!.ShouldContain("Refresh aggregates");
    }

    [Fact]
    public async Task Bars_that_fail_the_bar_checks_are_not_stored_and_are_counted()
    {
        Store(ThreeHoles);
        _source.Respond = t => new IntradayResult(t.InstrumentId, [BarAt(Minute(Wed, ThreeHoles[0]), 250), new Bar(Minute(Wed, ThreeHoles[1]), -10, -5, -20, -10, 100)], 1);

        var result = await RunAsync(Wed, Thu);

        (result.Value.Inserted, result.Value.Rejected, result.Value.MissingAfter).ShouldBe((1L, 1, 2));
    }

    [Fact]
    public async Task A_store_failure_is_a_result_not_an_exception_and_the_job_ends_failed()
    {
        Store(ThreeHoles);
        ServeHoles();
        _store.Failures[_id] = new InvalidOperationException("duplicate key");

        var result = await RunAsync(Wed, Thu);

        result.Error.Code.ShouldBe("data.repair_store_failed");
        _jobs.Jobs.Values.Single().Status.ShouldBe(DataJobStatus.Failed);
        await _start.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }
}
