#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Learning.Jobs;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning.Jobs;

/// <summary>
/// Spec P8-18 Part B, criteria 7 to 10 over fakes of the existing paths (simulation, store, experiment): the stages, the replay time measured with a fake clock, a cancel between instruments and at the last safe point storing nothing, a failure that is not a trial, a timeout,
/// a host stop that leaves the job running for the next start to interrupt, the throttle of the progress events and the single final event.
/// </summary>
public sealed class ExperimentJobRunnerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Start = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);

    private readonly JobFixtures.FakeJobs _jobs = new();

    private readonly JobFixtures.RecordingProgress _progress = new();

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();

    private readonly ICommandHandler<StoreRunCommand, StoreOutcome> _store = Substitute.For<ICommandHandler<StoreRunCommand, StoreOutcome>>();

    private readonly ICommandHandler<RunExperimentCommand, RunExperimentOutcome> _experiment = Substitute.For<ICommandHandler<RunExperimentCommand, RunExperimentOutcome>>();

    private readonly IHeavyJobProbe _heavy = Substitute.For<IHeavyJobProbe>();

    private readonly IRunRecordWriter _writer = Substitute.For<IRunRecordWriter>();

    private readonly ExperimentJobSettings _settings = new();

    private readonly SimulationRun _run = FabricatedRun.Assemble(FabricatedRun.List((JobFixtures.InstrumentA, "A"), (JobFixtures.InstrumentB, "B")), [FabricatedRun.Ran(JobFixtures.InstrumentA, "A", FabricatedRun.Monday, FabricatedRun.Monday), FabricatedRun.Ran(JobFixtures.InstrumentB, "B", FabricatedRun.Monday, FabricatedRun.Monday)]);

    public ExperimentJobRunnerTests()
    {
        _runs.FindAsync(JobFixtures.BaselineKey, Arg.Any<CancellationToken>()).Returns(JobFixtures.Header());
        _runs.ReadRecordTextAsync(JobFixtures.BaselineKey, Arg.Any<CancellationToken>()).Returns(JobFixtures.Record());
        _writer.Write(Arg.Any<SimulationRun>()).Returns(new RunRecordText("{}", new string('a', 64), new string('b', 64)));
        _store.HandleAsync(Arg.Any<StoreRunCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(StoreOutcome.Stored(1, 0)));
        _experiment.HandleAsync(Arg.Any<RunExperimentCommand>(), Arg.Any<CancellationToken>()).Returns(call => Result.Success(new RunExperimentOutcome(77, false, "Inconclusive", 1, [], "text", 12)));
        _heavy.AnyOtherRunningAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(false));
    }

    private static Func<NSubstitute.Core.CallInfo, ValueTask<Result<SimulationRun>>> Async(Func<NSubstitute.Core.CallInfo, Task<Result<SimulationRun>>> work) => call => new ValueTask<Result<SimulationRun>>(work(call));

    private ExperimentJobRunner Runner(TimeProvider? time = null) =>
        new(_jobs, _runs, new ReplayPlanner(JobFixtures.Engine(exits => exits is null), JobFixtures.Source(null)), _simulation, _writer, _store, _experiment, _progress, _heavy, _settings, time ?? _clock, NullLogger<ExperimentJobRunner>.Instance);

    private async Task<ExperimentJob> QueuedAsync()
    {
        var plan = new NewExperimentJob(Guid.NewGuid(), new string('1', 64), JobFixtures.BaselineKey, "exit", "FIXED_RR.rr", "2", "3", 6, "why", JobFixtures.Owner, 2, Start);
        (await _jobs.InsertAsync(plan, 3, TimeSpan.FromHours(24), Ct)).Status.ShouldBe(ExperimentJobInsertStatus.Created);
        return (await _jobs.ClaimNextAsync("xd-api", Start, Ct))!;
    }

    private ExperimentJob Row(Guid id) => _jobs.Rows.Single(r => r.Id == id);

    [Fact]
    public async Task A_job_replays_the_candidate_stores_it_as_the_jobs_and_records_one_experiment_as_the_owner_with_the_replay_time_measured_and_one_final_event()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            ((IProgress<int>)call.Arg<RunSimulationQuery>().Progress!).Report(2);
            _clock.Advance(TimeSpan.FromSeconds(90));
            return Result.Success(_run);
        });
        var job = await QueuedAsync();

        await Runner().ExecuteAsync(job, Ct);

        var done = Row(job.Id);
        done.State.ShouldBe(ExperimentJobState.Succeeded);
        done.CandidateRunKey.ShouldBe(_run.RunKey);
        done.ExperimentId.ShouldBe(77);
        done.ReplayMs.ShouldBe(90_000);
        done.CompareMs.ShouldBe(12);
        done.ErrorCode.ShouldBeNull();
        await _store.Received(1).HandleAsync(Arg.Is<StoreRunCommand>(c => c.Actor == "experiment:" + job.Id.ToString("D") && c.Run == _run), Arg.Any<CancellationToken>());
        await _experiment.Received(1).HandleAsync(
            Arg.Is<RunExperimentCommand>(c => c.Actor == JobFixtures.Owner && c.BaselineRunKey == JobFixtures.BaselineKey && c.CandidateRunKey == _run.RunKey && c.HoldOutMonths == 6 && c.Knob.Name == "FIXED_RR.rr" && c.Knob.Kind == ExperimentKnobKind.Exit && c.Knob.CandidateValue == "3"),
            Arg.Any<CancellationToken>());
        _progress.Finished.ShouldBe([new ExperimentJobFinished(job.Id, "succeeded")]);
    }

    [Fact]
    public async Task The_request_the_simulation_gets_is_the_baseline_with_exactly_the_one_knob_changed()
    {
        SimulationRequest? seen = null;
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            seen = call.Arg<RunSimulationQuery>().Request;
            return Result.Success(_run);
        });
        var job = await QueuedAsync();

        await Runner().ExecuteAsync(job, Ct);

        var plan = (await new ReplayPlanner(JobFixtures.Engine(exits => exits is null), JobFixtures.Source(null)).PlanAsync(JobFixtures.Header(), JobFixtures.Record(), Ct)).Value;
        JobFixtures.Describe(seen!).ShouldBe(JobFixtures.Describe(plan.BaselineRequest() with { Exits = new ExitChoice([new ExitCandidate("FIXED_RR", [new ExitParameterValue("rr", [3])])], null) }));
    }

    [Fact]
    public async Task A_refusal_of_the_simulation_fails_the_job_with_its_code_stores_nothing_and_records_no_experiment()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<SimulationRun>(Error.Validation("simulation.invalid", "bad")));
        var job = await QueuedAsync();

        await Runner().ExecuteAsync(job, Ct);

        Row(job.Id).State.ShouldBe(ExperimentJobState.Failed);
        Row(job.Id).ErrorCode.ShouldBe("simulation.invalid");
        Row(job.Id).ExperimentId.ShouldBeNull();
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _experiment.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        _progress.Finished.Single().State.ShouldBe("failed");
    }

    [Fact]
    public async Task When_the_judge_refuses_entries_the_baseline_lacks_the_job_fails_with_the_ledgers_code_the_candidate_run_stays_and_no_experiment_exists()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(_run));
        _experiment.HandleAsync(Arg.Any<RunExperimentCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<RunExperimentOutcome>(Error.Validation("experiments.entries_added", "the candidate has 3 entries the baseline lacks")));
        var job = await QueuedAsync();

        await Runner().ExecuteAsync(job, Ct);

        var done = Row(job.Id);
        done.State.ShouldBe(ExperimentJobState.Failed);
        done.ErrorCode.ShouldBe("experiments.entries_added");
        done.ErrorText!.ShouldContain("3 entries");
        done.CandidateRunKey.ShouldBe(_run.RunKey);
        done.ExperimentId.ShouldBeNull();
        await _store.Received(1).HandleAsync(Arg.Any<StoreRunCommand>(), Arg.Any<CancellationToken>());
        ExperimentJobViews.Of(done, null).Limits.ShouldContain("Judged nothing; not counted as a trial.");
    }

    [Fact]
    public async Task A_universe_that_is_not_the_baselines_fails_the_job_and_stores_nothing()
    {
        var other = FabricatedRun.Assemble(FabricatedRun.List((JobFixtures.InstrumentA, "A")), [FabricatedRun.Ran(JobFixtures.InstrumentA, "A", FabricatedRun.Monday, FabricatedRun.Monday)]);
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(other));
        var job = await QueuedAsync();

        await Runner().ExecuteAsync(job, Ct);

        Row(job.Id).ErrorCode.ShouldBe("experiments.universe_changed");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_cancel_asked_while_the_replay_runs_stops_it_between_instruments_and_nothing_is_stored_or_judged()
    {
        var started = new TaskCompletionSource();
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Async(async call =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            return Result.Success(_run);
        }));
        var job = await QueuedAsync();

        var running = Runner(TimeProvider.System).ExecuteAsync(job, Ct);
        await started.Task;
        (await _jobs.RequestCancelAsync(job.Id, Start, Ct)).ShouldBe(ExperimentJobCancelOutcome.Requested);
        await running;

        Row(job.Id).State.ShouldBe(ExperimentJobState.Cancelled);
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _experiment.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        _progress.Finished.ShouldBe([new ExperimentJobFinished(job.Id, "cancelled")]);
    }

    [Fact]
    public async Task A_cancel_that_arrives_after_the_replay_ended_is_seen_at_the_last_safe_point_before_the_store_is_written()
    {
        var job = await QueuedAsync();
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Async(async call =>
        {
            await _jobs.RequestCancelAsync(job.Id, Start, Ct);
            return Result.Success(_run);
        }));

        await Runner().ExecuteAsync(job, Ct);

        Row(job.Id).State.ShouldBe(ExperimentJobState.Cancelled);
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await _experiment.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_job_past_the_time_limit_fails_with_the_timeout_code_and_stores_nothing()
    {
        var started = new TaskCompletionSource();
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Async(async call =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            return Result.Success(_run);
        }));
        var job = await QueuedAsync();

        var running = Runner().ExecuteAsync(job, Ct);
        await started.Task;
        _clock.Advance(TimeSpan.FromMinutes(_settings.MaxMinutes + 1));
        await running;

        Row(job.Id).State.ShouldBe(ExperimentJobState.Failed);
        Row(job.Id).ErrorCode.ShouldBe("experiments.timeout");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task A_stop_of_the_host_leaves_the_job_running_with_no_final_event_and_the_next_start_marks_it_interrupted_never_resumed()
    {
        using var stop = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Async(async call =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            return Result.Success(_run);
        }));
        var job = await QueuedAsync();

        var running = Runner().ExecuteAsync(job, stop.Token);
        await started.Task;
        await stop.CancelAsync();
        await running;

        Row(job.Id).State.ShouldBe(ExperimentJobState.Running);
        _progress.Finished.ShouldBeEmpty();
        (await _jobs.InterruptRunningAsync("xd-api", Start, Ct)).ShouldBe([job.Id]);
        Row(job.Id).State.ShouldBe(ExperimentJobState.Interrupted);
    }

    [Fact]
    public async Task An_unexpected_exception_fails_the_job_with_a_code_and_never_leaks_its_text()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns<Result<SimulationRun>>(_ => throw new InvalidOperationException("secret detail"));
        var job = await QueuedAsync();

        await Runner().ExecuteAsync(job, Ct);

        Row(job.Id).ErrorCode.ShouldBe("experiments.job_crashed");
        Row(job.Id).ErrorText!.ShouldNotContain("secret");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Other_heavy_jobs_seen_while_the_replay_runs_are_noted_on_the_job_and_the_progress_events_carry_the_job()
    {
        var seen = new TaskCompletionSource();
        _heavy.AnyOtherRunningAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            seen.TrySetResult();
            return ValueTask.FromResult(true);
        });
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Async(async call =>
        {
            ((IProgress<int>)call.Arg<RunSimulationQuery>().Progress!).Report(1);
            await _jobs.HeavyMarked.Task; // the replay lasts until the watcher has noted it: no wall-clock assertion
            return Result.Success(_run);
        }));
        var job = await QueuedAsync();

        await Runner(TimeProvider.System).ExecuteAsync(job, Ct);

        Row(job.Id).OtherHeavyJobsSeen.ShouldBeTrue();
        Row(job.Id).State.ShouldBe(ExperimentJobState.Succeeded);
        _progress.Progress.ShouldAllBe(p => p.JobId == job.Id && p.State == "running");
    }

    [Fact]
    public void The_progress_throttle_passes_at_most_four_events_a_second_on_a_fake_clock()
    {
        var clock = new FakeTimeProvider(Start);
        var throttle = new XDrishti.Application.Lab.ProgressThrottle(clock, XDrishti.Application.Lab.ProgressThrottle.QuarterSecond);
        var passed = 0;
        for (var i = 0; i < 100; i++)
        {
            if (throttle.TryPass())
            {
                passed++;
            }

            clock.Advance(TimeSpan.FromMilliseconds(10)); // 100 attempts over exactly one second
        }

        passed.ShouldBe(4);
    }

    [Fact]
    public async Task A_failing_progress_report_or_cancel_read_never_stops_the_job_from_ending_in_a_final_state_and_a_new_job_can_start()
    {
        _jobs.FailReport = true;
        _jobs.FailIsCancel = true;
        var seen = new TaskCompletionSource();
        _heavy.AnyOtherRunningAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            seen.TrySetResult();
            return ValueTask.FromResult(false);
        });
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Async(async _ =>
        {
            await Task.Delay(1500, Ct); // long enough for the watcher to fail on its reports and its cancel reads
            return Result.Success(_run);
        }));
        var job = await QueuedAsync();

        await Runner(TimeProvider.System).ExecuteAsync(job, Ct);

        ExperimentJobStates.IsFinal(Row(job.Id).State).ShouldBeTrue("the watcher's failures never strand the job in running");
        _jobs.FailReport = false;
        _jobs.FailIsCancel = false;
        var next = new NewExperimentJob(Guid.NewGuid(), new string('2', 64), JobFixtures.BaselineKey, "exit", "FIXED_RR.rr", "2", "4", 6, "again", JobFixtures.Owner, 2, Start);
        (await _jobs.InsertAsync(next, 10, TimeSpan.FromHours(24), Ct)).Status.ShouldBe(ExperimentJobInsertStatus.Created, "nothing is stranded in running");
    }

    [Fact]
    public async Task A_finish_that_fails_twice_is_tried_again_and_the_job_ends()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(_run));
        var job = await QueuedAsync();
        _jobs.FinishFailures = 2;

        await Runner(TimeProvider.System).ExecuteAsync(job, Ct);

        Row(job.Id).State.ShouldBe(ExperimentJobState.Succeeded);
        _progress.Finished.ShouldBe([new ExperimentJobFinished(job.Id, "succeeded")]);
    }

    [Fact]
    public async Task A_finish_that_keeps_failing_throws_to_the_dispatcher_which_marks_the_job_interrupted_so_a_new_job_can_start()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(_run));
        var job = await QueuedAsync();
        _jobs.FinishFailures = 99;

        await Should.ThrowAsync<InvalidOperationException>(() => Runner(TimeProvider.System).ExecuteAsync(job, Ct));

        Row(job.Id).State.ShouldBe(ExperimentJobState.Running, "the runner could not end it");
        _jobs.FinishFailures = 0;
        (await _jobs.InterruptAsync(job.Id, Start, Ct)).ShouldBeTrue(); // what the dispatcher does when the runner throws
        Row(job.Id).State.ShouldBe(ExperimentJobState.Interrupted);
        var next = new NewExperimentJob(Guid.NewGuid(), new string('2', 64), JobFixtures.BaselineKey, "exit", "FIXED_RR.rr", "2", "4", 6, "again", JobFixtures.Owner, 2, Start);
        (await _jobs.InsertAsync(next, 10, TimeSpan.FromHours(24), Ct)).Status.ShouldBe(ExperimentJobInsertStatus.Created);
    }

    [Theory]
    [InlineData(214, 9 * 60_000, "met")]
    [InlineData(214, 10 * 60_000, "not_met")]
    [InlineData(213, 1_000, "not_comparable")]
    [InlineData(2, 90_000, "not_comparable")]
    public void The_replay_time_is_printed_with_the_instrument_count_and_the_target_is_judged_only_at_214_instruments(int instruments, long ms, string status)
    {
        var row = new ExperimentJob(Guid.NewGuid(), "k", JobFixtures.BaselineKey, "exit", "FIXED_RR.rr", "2", "3", 6, "r", ExperimentJobState.Succeeded, "compare", 0, 0, false, null, null, null, null, instruments, ms, 5, false, JobFixtures.Owner, Start, Start, Start, "xd-api", Start);

        var timing = ExperimentJobViews.Of(row, JobFixtures.Header()).Timing!;

        timing.TargetStatus.ShouldBe(status);
        timing.Text.ShouldContain($"for {instruments} instruments over 2024-04-01 to 2026-04-30, measured on this machine; other heavy jobs were running: no.");
        timing.Text.ShouldContain(status == "not_comparable" ? "not comparable" : "target for 214 instruments");
        ExperimentJobViews.Of(row with { OtherHeavyJobsSeen = true }, null).Timing!.Text.ShouldContain("other heavy jobs were running: yes");
        ExperimentJobViews.Of(row with { ReplayMs = null }, null).Timing.ShouldBeNull();
    }
}
