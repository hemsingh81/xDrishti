using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Tests.Market.Actions;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Application.Tests.Market.Quality;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars.Review;

/// <summary>Undoing the load of a CSV file (ADR 0017 §6): the command that starts the job, and the job's CSV phase that removes the bars and writes the audit event.</summary>
public sealed class CsvUndoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeDataJobStore _jobs = new();
    private readonly IDataJobChannel _queue = Substitute.For<IDataJobChannel>();
    private readonly IInstrumentCatalog _catalog = Substitute.For<IInstrumentCatalog>();
    private readonly FakeCsvFolder _folder = new();
    private readonly FakeCsvFileLog _log = new();
    private readonly FakeListedInstruments _listed = new();
    private readonly FakeBarStore _bars = new();
    private readonly FakeCsvUndo _undo = new();
    private readonly InMemoryTrackingData _tracking = new();
    private readonly FakeAggregates _aggregates = new();
    private readonly FakeDataSettings _settings = new();
    private readonly Guid _abb = Guid.CreateVersion7();

    public CsvUndoTests() => _catalog.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StartDataJobHandler Start => new(_jobs, _queue, _catalog, _folder, _clock, NullLogger<StartDataJobHandler>.Instance);

    private UndoCsvLoadHandler Handler => new(_log, Start);

    private CsvUndoRunner Runner(IMarketUnitOfWork? unitOfWork = null) => new(_undo, _log, _tracking, unitOfWork ?? _tracking, _clock, NullLogger<CsvUndoRunner>.Instance);

    private ExecuteDataJobHandler Execute => new(
        _jobs,
        new CsvPhaseRunner(_folder, _log, _listed, _bars, _catalog, new InstrumentTracking(_tracking, _tracking), _tracking, _settings, _clock, NullLogger<CsvPhaseRunner>.Instance),
        Runner(),
        new DhanPhaseRunner(new FakeIntradaySource(), FakeAccountStatus.Connected(), new FakeFetchTargets(), _bars, _settings, new FixedCalendar(), new GapBackfill(new FakeBarDayCounts(_bars), new FakeBarGaps(), _settings, _clock), _clock, NullLogger<DhanPhaseRunner>.Instance),
        new AggregatePhaseRunner(_aggregates, new FixedCalendar(), _settings, new CorporateActionDetection(new FakeDailySeries(), new InMemoryActionData(), new InMemoryActionData(), _clock), _clock, NullLogger<AggregatePhaseRunner>.Instance),
        new QualityPhaseRunner(new FakeQualityData(), new FakeQualityRuns(), _bars, new FakeBarGaps(), new FixedCalendar(), _settings, _clock, NullLogger<QualityPhaseRunner>.Instance),
        new FakeCalendarUpkeep(),
        _clock,
        NullLogger<ExecuteDataJobHandler>.Instance);

    private void Loaded(string name = "ABB_1m.csv.gz", long bars = 375)
    {
        var info = _folder.Add(name, FakeCsvFolder.Csv(1));
        _log.Loaded[name] = new LoadedCsvFile(name, info.Size, info.ModifiedAt, _abb, bars, bars, 0, 0, 0, Now);
        _undo.Loaded[name] = new CsvUndoResult(_abb, Now.AddDays(-1), Now, bars);
    }

    private async Task<DataJobView> StartedAsync(string name = "ABB_1m.csv.gz")
    {
        var result = await Handler.HandleAsync(new UndoCsvLoadCommand(name, "owner@example.invalid"), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public async Task Undoing_a_loaded_file_starts_a_job_about_its_instrument_that_names_the_file_and_runs_the_csv_then_the_aggregate_step()
    {
        Loaded();

        var view = await StartedAsync();

        view.Steps.ShouldBe(["csv", "aggregate"]);
        (view.UndoCsvFile, view.InstrumentId, view.Status, view.RequestedBy).ShouldBe(("ABB_1m.csv.gz", _abb, "running", "owner@example.invalid"));
        (view.ForceCsv, view.RebuildAll, view.CsvFiles).ShouldBe((false, false, null));
        _queue.Received(1).Enqueue(new DataJobTicket(view.Id));
        _undo.Undone.ShouldBeEmpty(); // the removal is the job's work, not the request's
    }

    [Theory]
    [InlineData("TCS_1m.csv.gz")]
    [InlineData("../ABB_1m.csv.gz")]
    [InlineData("ABB.csv")]
    [InlineData("")]
    public async Task A_file_that_was_never_loaded_or_has_no_valid_name_is_not_found_and_no_job_starts(string name)
    {
        Loaded();

        var result = await Handler.HandleAsync(new UndoCsvLoadCommand(name, "owner"), Ct);

        result.Error.Code.ShouldBe("data.csv_file_not_found");
        _jobs.Started.ShouldBeEmpty();
        _queue.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task A_loaded_file_without_an_instrument_cannot_be_undone()
    {
        Loaded();
        _log.Loaded["ABB_1m.csv.gz"] = _log.Loaded["ABB_1m.csv.gz"] with { InstrumentId = null };

        var result = await Handler.HandleAsync(new UndoCsvLoadCommand("ABB_1m.csv.gz", "owner"), Ct);

        result.Error.Code.ShouldBe("data.csv_file_not_found");
    }

    [Fact]
    public async Task While_a_data_job_runs_the_undo_is_refused_with_409_and_removes_nothing()
    {
        Loaded();
        await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate"]), "owner"), Ct);
        _queue.ClearReceivedCalls();

        var result = await Handler.HandleAsync(new UndoCsvLoadCommand("ABB_1m.csv.gz", "owner"), Ct);

        result.Error.Code.ShouldBe("data.job_running");
        _queue.DidNotReceiveWithAnyArgs().Enqueue(default!);
        _undo.Undone.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_never_loaded_file_is_404_even_while_a_job_runs()
    {
        await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate"]), "owner"), Ct);

        var result = await Handler.HandleAsync(new UndoCsvLoadCommand("ABB_1m.csv.gz", "owner"), Ct);

        result.Error.Code.ShouldBe("data.csv_file_not_found");
    }

    [Fact]
    public async Task The_job_removes_the_bars_audits_the_undo_and_rebuilds_the_aggregates_of_that_instrument_only()
    {
        Loaded(bars: 375);
        var other = Guid.CreateVersion7();
        _aggregates.Dirty.Add(new DirtyInstrument(_abb, "NSE:ABB", Now.AddDays(-1), Now));
        _aggregates.Dirty.Add(new DirtyInstrument(other, "NSE:TCS", Now, Now));
        var view = await StartedAsync();

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Error).ShouldBe((DataJobStatus.Succeeded, null));
        job.Csv.Status.ShouldBe(PhaseStatus.Succeeded);
        (job.Csv.BarsRemoved, job.Csv.Done, job.Csv.Total).ShouldBe((375, 1, 1));
        job.Csv.Message.ShouldBe("CSV load of ABB_1m.csv.gz undone, 375 bars removed");
        _undo.Undone.ShouldBe(["ABB_1m.csv.gz"]);
        _folder.Opened.ShouldBeEmpty(); // the undo never reads the file
        var audit = _tracking.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail, audit.Actor, audit.AccountId).ShouldBe((AccountEventType.CsvLoadUndone, "CSV load of ABB_1m.csv.gz undone, 375 bars removed", "owner@example.invalid", null));
        job.Aggregate.Status.ShouldBe(PhaseStatus.Succeeded);
        _aggregates.Built.Select(b => b.InstrumentId).ShouldBe([_abb]);
        job.Dhan.Status.ShouldBe(PhaseStatus.NotRequested);
    }

    [Fact]
    public async Task When_another_loaded_file_of_the_instrument_overlaps_the_range_the_job_removes_the_bars_and_warns_naming_the_files()
    {
        Loaded();
        var copy = _folder.Add("ABB_copy_1m.csv.gz", FakeCsvFolder.Csv(1));
        var self = _log.Loaded["ABB_1m.csv.gz"] with { FirstBar = Now.AddDays(-1), LastBar = Now };
        _log.Loaded["ABB_1m.csv.gz"] = self;
        _log.Loaded[copy.Name] = new LoadedCsvFile(copy.Name, copy.Size, copy.ModifiedAt, _abb, 375, 375, 0, 0, 0, Now, Now.AddHours(-3), Now.AddHours(1));
        var other = Guid.CreateVersion7();
        _log.Loaded["TCS_1m.csv.gz"] = new LoadedCsvFile("TCS_1m.csv.gz", 1, Now, other, 1, 1, 0, 0, 0, Now, Now.AddDays(-1), Now); // another instrument: no overlap
        var view = await StartedAsync();

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Csv.BarsRemoved).ShouldBe((DataJobStatus.Succeeded, 375));
        job.Warnings.ShouldHaveSingleItem().ShouldBe(CsvUndoRunner.OverlapWarning(["ABB_copy_1m.csv.gz"]));
        job.Warnings[0].ShouldContain("ABB_copy_1m.csv.gz");
        _undo.Undone.ShouldBe(["ABB_1m.csv.gz"]); // the undo still removes the instrument's bars in the file's range
    }

    [Fact]
    public async Task Without_an_overlapping_file_the_undo_has_no_warning()
    {
        Loaded();
        var view = await StartedAsync();

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _jobs.Jobs[view.Id].Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_audit_text_has_the_file_and_the_count_and_nothing_else()
    {
        Loaded(bars: 1);
        var view = await StartedAsync();

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _tracking.Events.Single().Detail.ShouldBe("CSV load of ABB_1m.csv.gz undone, 1 bars removed");
    }

    [Fact]
    public async Task A_file_no_longer_loaded_when_the_job_runs_fails_the_csv_phase_with_the_code_and_still_rebuilds()
    {
        Loaded();
        var view = await StartedAsync();
        _undo.Loaded.Clear();

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Csv.Status, job.Csv.Code).ShouldBe((DataJobStatus.Failed, PhaseStatus.Failed, "data.csv_file_not_found"));
        job.Aggregate.Status.ShouldBe(PhaseStatus.Succeeded);
        _tracking.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_database_failure_during_the_removal_is_a_generic_message_and_no_audit_event()
    {
        Loaded();
        _undo.Failure = new InvalidOperationException("password=hunter2 in the connection string");
        var view = await StartedAsync();

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Csv.Status).ShouldBe((DataJobStatus.Failed, PhaseStatus.Failed));
        job.Error!.ShouldNotContain("hunter2");
        job.Csv.Message!.ShouldNotContain("hunter2");
        _tracking.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_audit_cannot_be_written_the_undo_stands_and_the_job_has_a_warning()
    {
        Loaded();
        var failing = new RefusingUnitOfWork();
        var job = DataJob.StartUndo("ABB_1m.csv.gz", _abb, "owner", Now);
        _jobs.Jobs[job.Id] = job;

        var phase = await Runner(failing).RunAsync(job, new JobReporter(job, _jobs, _clock), Ct);

        (phase.Status, phase.BarsRemoved).ShouldBe((PhaseStatus.Succeeded, 375));
        job.Warnings.ShouldBe(["The undo is done but could not be written to the activity log."]);
    }

    [Fact]
    public async Task The_runner_refuses_a_job_that_does_not_undo_a_file()
    {
        var job = DataJob.Start([DataSteps.Csv], false, "owner", Now);

        await Should.ThrowAsync<InvalidOperationException>(async () => await Runner().RunAsync(job, new JobReporter(job, _jobs, _clock), Ct));
    }

    private sealed class RefusingUnitOfWork : IMarketUnitOfWork
    {
        public ValueTask SaveAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("db down");

        public ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("db down");
    }
}
