using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Tests.Market.Actions;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Application.Tests.Market.Context;
using XDrishti.Application.Tests.Market.Quality;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>Starting, running and recovering data jobs, and reading them back.</summary>
public sealed class DataJobHandlersTests
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
    private readonly FakeIntradaySource _source = new();
    private readonly FakeAccountStatus _account = FakeAccountStatus.Connected();
    private readonly FakeFetchTargets _targets = new();
    private readonly FakeAggregates _aggregates = new();
    private readonly FakeDataSettings _settings = new();
    private readonly FakeCalendarUpkeep _calendar = new();
    private readonly FakeContextUpkeep _context = new();
    private readonly FakeQualityData _qualityData = new();
    private readonly FakeQualityRuns _qualityRuns = new();

    public DataJobHandlersTests() => _catalog.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StartDataJobHandler Start => new(_jobs, _queue, _catalog, _folder, _clock, NullLogger<StartDataJobHandler>.Instance);

    private ExecuteDataJobHandler Execute => new(
        _jobs,
        new CsvPhaseRunner(_folder, _log, _listed, _bars, _catalog, new InstrumentTracking(_tracking, _tracking), _tracking, _settings, _clock, NullLogger<CsvPhaseRunner>.Instance),
        new CsvUndoRunner(_undo, new FakeCsvFileLog(), _tracking, _tracking, _clock, NullLogger<CsvUndoRunner>.Instance),
        new DhanPhaseRunner(_source, _account, _targets, _bars, _settings, new FixedCalendar(), new GapBackfill(new FakeBarDayCounts(_bars), new FakeBarGaps(), _settings, _clock), _clock, NullLogger<DhanPhaseRunner>.Instance),
        new AggregatePhaseRunner(_aggregates, new FixedCalendar(), _settings, new CorporateActionDetection(new FakeDailySeries(), new InMemoryActionData(), new InMemoryActionData(), _clock), _clock, NullLogger<AggregatePhaseRunner>.Instance),
        new QualityPhaseRunner(_qualityData, _qualityRuns, _bars, new FakeBarGaps(), new FixedCalendar(), _settings, _clock, NullLogger<QualityPhaseRunner>.Instance),
        _calendar,
        _context,
        _clock,
        NullLogger<ExecuteDataJobHandler>.Instance);

    private async Task<DataJobView> StartedAsync(params string[] steps)
    {
        var result = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(steps.Length == 0 ? null : steps), "owner@example.invalid"), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public async Task Starting_registers_the_job_queues_it_and_returns_it_with_every_step_pending()
    {
        var view = await StartedAsync();

        view.Steps.ShouldBe(["csv", "dhan", "aggregate"]);
        (view.Status, view.RequestedBy, view.StartedAt).ShouldBe(("running", "owner@example.invalid", Now));
        _jobs.Started.ShouldHaveSingleItem().Id.ShouldBe(view.Id);
        _queue.Received(1).Enqueue(new DataJobTicket(view.Id));
    }

    [Fact]
    public async Task A_second_start_while_one_runs_is_refused_and_not_queued()
    {
        await StartedAsync();
        _queue.ClearReceivedCalls();

        var second = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(), "owner"), Ct);

        second.Error.Code.ShouldBe("data.job_running");
        _queue.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    public static TheoryData<string[]> BadSteps
    {
        get
        {
            var data = new TheoryData<string[]>();
            data.Add([]);
            data.Add(["csv", "bogus"]);
            data.Add(["CSV"]);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(BadSteps))]
    public async Task Missing_or_unknown_steps_are_refused_with_the_field(string[] steps)
    {
        var result = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(steps), "owner"), Ct);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.ShouldHaveSingleItem().Field.ShouldBe("steps");
        _jobs.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_csv_job_needs_the_instrument_list_but_the_other_steps_do_not()
    {
        _catalog.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(0);

        var csv = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["csv"]), "owner"), Ct);
        var all = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(), "owner"), Ct);
        var dhan = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["dhan", "aggregate"]), "owner"), Ct);

        csv.Error.Code.ShouldBe("data.master_missing");
        all.Error.Code.ShouldBe("data.master_missing");
        dhan.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Forcing_csv_is_kept_on_the_job_only_with_the_csv_step()
    {
        var forced = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["csv"], true), "owner"), Ct);

        forced.Value.ForceCsv.ShouldBeTrue();
    }

    [Fact]
    public async Task A_job_started_by_the_endpoint_is_manual_and_the_view_says_so()
    {
        var view = await StartedAsync("dhan");

        (view.Trigger, view.RefetchGaps).ShouldBe(("manual", false));
    }

    [Fact]
    public async Task Re_fetching_gaps_is_kept_on_the_job_only_with_the_dhan_step()
    {
        var withDhan = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["dhan", "aggregate"], null, true), "owner"), Ct);
        _jobs.Jobs.Clear();
        var withoutDhan = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate"], null, true), "owner"), Ct);

        withDhan.Value.RefetchGaps.ShouldBeTrue();
        withoutDhan.Value.RefetchGaps.ShouldBeFalse();
    }

    [Fact]
    public async Task The_scheduled_run_starts_the_same_job_with_the_daily_trigger()
    {
        var result = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["dhan", "aggregate"]), "system:data-eod", DataJobTrigger.Daily), Ct);

        (result.Value.Trigger, result.Value.RequestedBy, result.Value.Status).ShouldBe(("daily", "system:data-eod", "running"));
        _jobs.Started.ShouldHaveSingleItem().Trigger.ShouldBe(DataJobTrigger.Daily);
    }

    [Fact]
    public async Task Executing_runs_the_requested_phases_in_order_and_ends_the_job_with_the_result()
    {
        var abb = _tracking.AddInstrument("NSE:ABB");
        _listed.Matches["NSE:ABB"] = [abb.Id];
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(4));
        _aggregates.Dirty.Add(new DirtyInstrument(abb.Id, "NSE:ABB", Now, Now));
        _account.State = new DataAccountState(null, null, DataAccountStatuses.None, "No account has the primary Data role.");
        var view = await StartedAsync();

        var result = await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        result.IsSuccess.ShouldBeTrue();
        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Error, job.CurrentPhase).ShouldBe((DataJobStatus.Succeeded, null, null));
        (job.Csv.Status, job.Dhan.Status, job.Aggregate.Status).ShouldBe((PhaseStatus.Succeeded, PhaseStatus.Skipped, PhaseStatus.Succeeded));
        (job.Csv.Rows, job.Aggregate.Done).ShouldBe((4, 1));
        job.FinishedAt.ShouldBe(Now);
        _jobs.Published[^1].Status.ShouldBe(DataJobStatus.Succeeded);
    }

    [Fact]
    public async Task The_calendar_upkeep_runs_after_the_phases_and_before_the_job_ends_and_its_warnings_join_the_jobs()
    {
        var view = await StartedAsync("aggregate");
        _calendar.Action = async reporter =>
        {
            _jobs.Jobs[view.Id].Aggregate.Status.ShouldBe(PhaseStatus.Succeeded);
            _jobs.Jobs[view.Id].IsRunning.ShouldBeTrue();
            await reporter.WarnAsync("Trading calendar: NSE's holiday list answered HTTP 403.", Ct);
        };

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _calendar.Runs.ShouldBe(1);
        var job = _jobs.Jobs[view.Id];
        job.Status.ShouldBe(DataJobStatus.Succeeded);
        job.Warnings.ShouldBe(["Trading calendar: NSE's holiday list answered HTTP 403."]);
    }

    [Fact]
    public async Task The_calendar_upkeep_also_runs_after_a_job_whose_phase_failed()
    {
        var view = await StartedAsync("dhan");
        _source.Respond = _ => throw new InvalidOperationException("boom");
        _targets.Targets.Add(new FetchTarget(Guid.NewGuid(), "NSE:ABB", "share", "1", "NSE_EQ", null));

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _calendar.Runs.ShouldBe(1);
        _jobs.Jobs[view.Id].Status.ShouldBe(DataJobStatus.Failed);
    }

    [Fact]
    public async Task The_context_upkeep_runs_after_the_calendar_upkeep_of_a_job_that_aggregated()
    {
        var view = await StartedAsync("aggregate");
        _calendar.Action = _ =>
        {
            _context.Runs.ShouldBe(0, "the calendar comes first: it may show a new holiday");
            return ValueTask.CompletedTask;
        };

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _context.Runs.ShouldBe(1);
        _jobs.Jobs[view.Id].Status.ShouldBe(DataJobStatus.Succeeded);
    }

    [Fact]
    public async Task The_context_upkeep_does_not_run_after_a_job_without_the_aggregate_step()
    {
        var view = await StartedAsync("dhan");

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _context.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task A_job_ended_elsewhere_gets_no_context_upkeep()
    {
        var view = await StartedAsync("aggregate");
        _jobs.EndedElsewhere.Add(view.Id);

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _context.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task A_job_ended_elsewhere_gets_no_calendar_upkeep()
    {
        var view = await StartedAsync("aggregate");
        _jobs.EndedElsewhere.Add(view.Id);

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _calendar.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task A_phase_that_crashes_is_failed_with_a_generic_message_and_the_later_phases_still_run()
    {
        var abb = _tracking.AddInstrument("NSE:ABB");
        _aggregates.Dirty.Add(new DirtyInstrument(abb.Id, "NSE:ABB", Now, Now));
        _folder.FolderExists = true;
        _catalog.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(10);
        _source.Respond = _ => throw new InvalidOperationException("token=abc123 leaked");
        _targets.Targets.Add(new FetchTarget(abb.Id, "NSE:ABB", "share", "1", "NSE_EQ", null));
        var view = await StartedAsync();

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Csv.Status, job.Dhan.Status, job.Aggregate.Status).ShouldBe((DataJobStatus.Failed, PhaseStatus.Succeeded, PhaseStatus.Failed, PhaseStatus.Succeeded));
        job.Error.ShouldBe("The update failed unexpectedly. See the API log.");
        job.Error!.ShouldNotContain("abc123");
        job.Dhan.Message!.ShouldNotContain("abc123");
    }

    [Fact]
    public async Task When_the_application_stops_the_job_is_failed_as_interrupted_with_the_waiting_phases_skipped()
    {
        var view = await StartedAsync();
        using var cancel = new CancellationTokenSource();
        _folder.FolderExists = true;
        _folder.Add("ABB_1m.csv", FakeCsvFolder.Csv(1));
        _listed.Add("NSE:ABB");
        _bars.Failures[_listed.Matches["NSE:ABB"][0]] = new OperationCanceledException(cancel.Token);
        await cancel.CancelAsync();

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), cancel.Token);

        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Error).ShouldBe((DataJobStatus.Failed, "The update was interrupted because the application is stopping."));
        (job.Csv.Status, job.Dhan.Status, job.Aggregate.Status).ShouldBe((PhaseStatus.Failed, PhaseStatus.Skipped, PhaseStatus.Skipped));
    }

    [Fact]
    public async Task A_job_that_was_ended_elsewhere_while_it_runs_stops_at_once_and_the_worker_does_not_finish_it()
    {
        var abb = _tracking.AddInstrument("NSE:ABB");
        _aggregates.Dirty.Add(new DirtyInstrument(abb.Id, "NSE:ABB", Now, Now));
        var view = await StartedAsync();
        _jobs.EndedElsewhere.Add(view.Id); // recovery (or the 12-hour rule) failed it in the store

        (await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct)).IsSuccess.ShouldBeTrue();

        _jobs.Published.ShouldBeEmpty();
        _aggregates.Built.ShouldBeEmpty();
        _source.Plans.ShouldBeEmpty();
        _jobs.Jobs[view.Id].Aggregate.Status.ShouldBe(PhaseStatus.Pending);
    }

    [Fact]
    public async Task Executing_an_unknown_or_ended_job_does_nothing()
    {
        var view = await StartedAsync();
        _jobs.Jobs[view.Id].Finish(Now);
        var published = _jobs.Published.Count;

        (await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct)).IsSuccess.ShouldBeTrue();
        (await Execute.HandleAsync(new ExecuteDataJobCommand(Guid.NewGuid()), Ct)).IsSuccess.ShouldBeTrue();

        _jobs.Published.Count.ShouldBe(published);
    }

    [Fact]
    public async Task Recovery_fails_the_jobs_that_began_before_the_process_and_leaves_the_others()
    {
        var old = await StartedAsync();
        var recover = new RecoverInterruptedDataJobsHandler(_jobs, _qualityRuns, _clock, NullLogger<RecoverInterruptedDataJobsHandler>.Instance);

        await recover.HandleAsync(new RecoverInterruptedDataJobsCommand(Now), Ct);
        _jobs.Jobs[old.Id].IsRunning.ShouldBeTrue(); // began exactly when the process did: not before
        await recover.HandleAsync(new RecoverInterruptedDataJobsCommand(Now.AddSeconds(1)), Ct);

        _jobs.Jobs[old.Id].Status.ShouldBe(DataJobStatus.Failed);
    }

    [Fact]
    public async Task The_history_is_newest_first_clamped_and_one_job_is_read_by_id()
    {
        var first = await StartedAsync("aggregate");
        _jobs.Jobs[first.Id].Finish(Now);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = await StartedAsync("csv");

        var list = new ListDataJobsHandler(_jobs);
        var all = await list.HandleAsync(new ListDataJobsQuery(null), Ct);
        var one = await list.HandleAsync(new ListDataJobsQuery(0), Ct);
        var get = new GetDataJobHandler(_jobs);

        all.Select(j => j.Id).ShouldBe([second.Id, first.Id]);
        one.Select(j => j.Id).ShouldBe([second.Id]);
        (await get.HandleAsync(new GetDataJobQuery(first.Id), Ct)).Value.Steps.ShouldBe(["aggregate"]);
        (await get.HandleAsync(new GetDataJobQuery(Guid.NewGuid()), Ct)).Error.Code.ShouldBe("data.job_not_found");
    }

    private QualityTarget QualityInstrument(string symbol = "NSE:ABB")
    {
        var bars = QualityBars.FullWindow();
        var target = new QualityTarget(Guid.CreateVersion7(), symbol, DateTimeOffset.FromUnixTimeSeconds(bars[0].EpochSeconds).AddDays(-60), DateTimeOffset.FromUnixTimeSeconds(bars[^1].EpochSeconds));
        _qualityData.Targets.Add(target);
        _bars.Stored[target.InstrumentId] = bars;
        _qualityData.Stored[target.InstrumentId] = [.. MinuteAggregator.Build(bars).Select(b => new StoredBucket(b.Key.Timeframe, b.Key.Start, b.Value.Open, b.Value.High, b.Value.Low, b.Value.Close, b.Value.Volume, b.Value.Bars))];
        return target;
    }

    [Fact]
    public async Task The_quality_step_is_accepted_and_its_phase_is_in_the_view_only_when_it_was_asked_for()
    {
        var withQuality = await StartedAsync("quality");
        _jobs.Jobs.Clear();
        var without = await StartedAsync("dhan");

        withQuality.Quality!.Status.ShouldBe(PhaseStatus.Pending);
        without.Quality.ShouldBeNull();
        withQuality.Steps.ShouldBe(["quality"]);
    }

    [Fact]
    public async Task Without_steps_the_job_does_not_include_the_quality_checks()
    {
        var view = await StartedAsync();

        view.Steps.ShouldNotContain("quality");
        view.Quality.ShouldBeNull();
    }

    [Fact]
    public async Task The_message_for_unknown_steps_names_all_four()
    {
        var result = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["bogus"]), "owner"), Ct);

        result.Error.Fields!.Single().Message.ShouldBe("Use csv, dhan, aggregate and quality.");
    }

    [Fact]
    public async Task A_job_about_one_instrument_carries_it_in_the_view()
    {
        var id = Guid.NewGuid();

        var result = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["quality"]), "owner", DataJobTrigger.Manual, id), Ct);

        result.Value.InstrumentId.ShouldBe(id);
        _jobs.Started.Single().InstrumentId.ShouldBe(id);
    }

    [Fact]
    public async Task The_quality_phase_runs_after_the_aggregates_and_the_job_succeeds_with_the_run_in_its_phase()
    {
        QualityInstrument();
        var abb = _tracking.AddInstrument("NSE:ABB");
        _aggregates.Dirty.Add(new DirtyInstrument(abb.Id, "NSE:ABB", Now, Now));
        var view = await StartedAsync("aggregate", "quality");
        _calendar.Action = _ =>
        {
            _jobs.Jobs[view.Id].Quality.Status.ShouldBe(PhaseStatus.Succeeded);
            return ValueTask.CompletedTask;
        };

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Aggregate.Status, job.Quality.Status).ShouldBe((DataJobStatus.Succeeded, PhaseStatus.Succeeded, PhaseStatus.Succeeded));
        (job.Quality.Done, job.Quality.Passed, job.Quality.RunId).ShouldBe((1L, 1L, _qualityRuns.Runs.Keys.Single()));
        _qualityRuns.Runs.Values.Single().Trigger.ShouldBe(QualityTrigger.AfterJob);
        _calendar.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task A_job_that_only_checks_the_quality_runs_no_calendar_upkeep()
    {
        QualityInstrument();
        var view = await StartedAsync("quality");

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        _jobs.Jobs[view.Id].Quality.Status.ShouldBe(PhaseStatus.Succeeded);
        _calendar.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task A_job_about_one_instrument_runs_no_calendar_upkeep_either()
    {
        var target = QualityInstrument();
        var started = await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate", "quality"]), "owner", DataJobTrigger.Manual, target.InstrumentId), Ct);

        await Execute.HandleAsync(new ExecuteDataJobCommand(started.Value.Id), Ct);

        _calendar.Runs.ShouldBe(0);
        _jobs.Jobs[started.Value.Id].Quality.Total.ShouldBe(1);
    }

    [Fact]
    public async Task A_quality_phase_that_crashes_is_failed_with_a_generic_message_and_the_job_fails()
    {
        QualityInstrument();
        _qualityRuns.SaveFailure = new InvalidOperationException("password=hunter2 leaked");
        var view = await StartedAsync("quality");

        await Execute.HandleAsync(new ExecuteDataJobCommand(view.Id), Ct);

        var job = _jobs.Jobs[view.Id];
        (job.Status, job.Quality.Status, job.Quality.Message).ShouldBe((DataJobStatus.Failed, PhaseStatus.Failed, "The update failed unexpectedly. See the API log."));
        job.Error!.ShouldNotContain("hunter2");
        _qualityRuns.Runs.Values.Single().Status.ShouldBe(QualityRunStatus.Failed);
    }

    [Fact]
    public async Task Recovery_also_fails_the_quality_runs_that_began_before_the_process()
    {
        var run = QualityRun.Start(QualityTrigger.Daily, 5, 214, null, null, Now.AddHours(-1));
        _qualityRuns.Runs[run.Id] = run;
        var recover = new RecoverInterruptedDataJobsHandler(_jobs, _qualityRuns, _clock, NullLogger<RecoverInterruptedDataJobsHandler>.Instance);

        await recover.HandleAsync(new RecoverInterruptedDataJobsCommand(Now), Ct);

        run.Status.ShouldBe(QualityRunStatus.Failed);
    }
}
