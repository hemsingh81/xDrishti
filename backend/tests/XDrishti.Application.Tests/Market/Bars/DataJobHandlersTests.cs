using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Market.Bars;

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
    private readonly InMemoryTrackingData _tracking = new();
    private readonly FakeIntradaySource _source = new();
    private readonly FakeAccountStatus _account = FakeAccountStatus.Connected();
    private readonly FakeFetchTargets _targets = new();
    private readonly FakeAggregates _aggregates = new();
    private readonly FakeDataSettings _settings = new();

    public DataJobHandlersTests() => _catalog.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StartDataJobHandler Start => new(_jobs, _queue, _catalog, _clock, NullLogger<StartDataJobHandler>.Instance);

    private ExecuteDataJobHandler Execute => new(
        _jobs,
        new CsvPhaseRunner(_folder, _log, _listed, _bars, _catalog, new InstrumentTracking(_tracking, _tracking), _tracking, _settings, _clock, NullLogger<CsvPhaseRunner>.Instance),
        new DhanPhaseRunner(_source, _account, _targets, _bars, _settings, _clock, NullLogger<DhanPhaseRunner>.Instance),
        new AggregatePhaseRunner(_aggregates, _clock, NullLogger<AggregatePhaseRunner>.Instance),
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
        var recover = new RecoverInterruptedDataJobsHandler(_jobs, _clock, NullLogger<RecoverInterruptedDataJobsHandler>.Instance);

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
}
