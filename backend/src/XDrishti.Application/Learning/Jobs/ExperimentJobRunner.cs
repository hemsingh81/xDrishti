using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>
/// Carries out a claimed replay job (spec P8-18 Part B, ADR 0129) by the paths that exist: the baseline's request is rebuilt with ONE field changed (<c>prepare</c>), run by <c>RunSimulationQuery</c> (<c>replay</c>), kept by <c>StoreRunCommand</c> as <c>experiment:&lt;job&gt;</c>
/// (<c>store</c>) and judged by <c>RunExperimentCommand</c> (<c>compare</c>): no second simulator, no second judge. The replay is in memory until it is complete; a stop asked for is seen between instruments and once more before the store is written, and then nothing is stored (no run, no
/// ledger row, not a trial). A failure before the judgement leaves any stored candidate run, records no experiment and counts no trial. The replay time is the wall time of the candidate replay only. Nothing here applies a change: the namespace names no store of strategies, settings,
/// plans, cells, models or costs (an architecture test).
/// </summary>
internal sealed class ExperimentJobRunner(
    IExperimentJobStore jobs,
    IRunStore runs,
    ReplayPlanner planner,
    IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation,
    IRunRecordWriter recordWriter,
    ICommandHandler<StoreRunCommand, StoreOutcome> storeRun,
    ICommandHandler<RunExperimentCommand, RunExperimentOutcome> experiment,
    IExperimentJobProgress progress,
    IHeavyJobProbe heavy,
    ExperimentJobSettings settings,
    TimeProvider time,
    ILogger<ExperimentJobRunner> logger) : IExperimentJobExecutor
{
    public const string Prepare = "prepare";
    public const string Replay = "replay";
    public const string Store = "store";
    public const string Compare = "compare";

    /// <summary>How often the watcher looks at the job.</summary>
    private static readonly TimeSpan WatchTick = TimeSpan.FromMilliseconds(100);

    /// <summary>How many ticks pass between two looks at the stop flag in the database.</summary>
    private const int CancelEvery = 5;

    /// <summary>Who is recorded as the author of the stored candidate run.</summary>
    public static string ActorOf(Guid jobId) => "experiment:" + jobId.ToString("D");

    private sealed class Watch
    {
        public int Done;
        public int Total;
        public string Stage = Prepare;
        public bool Heavy;
    }

    public async Task ExecuteAsync(ExperimentJob job, CancellationToken hostStop)
    {
        ArgumentNullException.ThrowIfNull(job);
        ExperimentJobLogEvents.Started(logger, job.Id);
        var watch = new Watch { Total = job.Instruments };
        using var timeout = new CancellationTokenSource();
        using var limit = time.CreateTimer(_ => timeout.Cancel(), null, TimeSpan.FromMinutes(settings.MaxMinutes), Timeout.InfiniteTimeSpan); // the job's own clock (a test moves it)
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(hostStop, timeout.Token);
        using var watcherStop = new CancellationTokenSource();
        var watcher = WatchAsync(job, watch, owner, watcherStop.Token);
        ExperimentJobFinish finish;
        try
        {
            finish = await StagesAsync(job, watch, owner.Token, hostStop);
        }
        catch (OperationCanceledException) when (hostStop.IsCancellationRequested)
        {
            await StopQuietlyAsync(watcherStop, watcher, job.Id);
            return; // the host is stopping: the job stays running and the next start marks it interrupted
        }
        catch (OperationCanceledException)
        {
            // the owner asked for a stop, or the job ran past its time
            ExperimentJobLogEvents.Cancelled(logger, job.Id, watch.Stage);
            finish = timeout.IsCancellationRequested
                ? Failed(job, watch, ExperimentJobErrors.TimeoutCode, "The replay ran past " + settings.MaxMinutes.ToString(CultureInfo.InvariantCulture) + " minutes and was stopped. Nothing was stored; not counted as a trial.")
                : new ExperimentJobFinish(job.Id, ExperimentJobState.Cancelled, watch.Stage, null, null, null, null, null, null, watch.Heavy, time.GetUtcNow());
        }
#pragma warning disable CA1031 // an unexpected failure is recorded on the job; the host goes on
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExperimentJobLogEvents.Crashed(logger, job.Id, watch.Stage, ex);
            finish = Failed(job, watch, ExperimentJobErrors.CrashedCode, "The job stopped on an unexpected error; see the log. Nothing was judged; not counted as a trial.");
        }

        // The job ALWAYS reaches a final state: a watcher that failed does not stop the finish, and the finish is tried again (a job left running would block every new start until a restart).
        await StopQuietlyAsync(watcherStop, watcher, job.Id);
        await FinishAsync(finish);
        var stateCode = ExperimentJobStates.Code(finish.State);
        ExperimentJobLogEvents.Finished(logger, job.Id, stateCode, finish.ReplayMs ?? 0, finish.CompareMs ?? 0);
        try
        {
            await progress.FinishedAsync(new ExperimentJobFinished(job.Id, stateCode), CancellationToken.None);
        }
#pragma warning disable CA1031 // a missed event costs nothing: the job is served by the API as well
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExperimentJobLogEvents.WatcherFailed(logger, job.Id, ex);
        }
    }

    private const int FinishAttempts = 3;

    private async Task FinishAsync(ExperimentJobFinish finish)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await jobs.FinishAsync(finish, CancellationToken.None);
                return;
            }
#pragma warning disable CA1031 // retried; the last failure goes to the dispatcher, which marks the job interrupted
            catch (Exception ex) when (attempt < FinishAttempts)
#pragma warning restore CA1031
            {
                ExperimentJobLogEvents.WatcherFailed(logger, finish.Id, ex);
                await Task.Delay(TimeSpan.FromMilliseconds(200), time, CancellationToken.None);
            }
        }
    }

    private async Task<ExperimentJobFinish> StagesAsync(ExperimentJob job, Watch watch, CancellationToken token, CancellationToken hostStop)
    {
        // prepare: the baseline as it was stored, one field changed
        var header = await runs.FindAsync(job.BaselineRunKey, token);
        var record = header is null ? null : await runs.ReadRecordTextAsync(job.BaselineRunKey, token);
        if (header is null || record is null)
        {
            return Failed(job, watch, LearningErrors.RunNotFoundCode, "The baseline run is not stored any more.");
        }

        var plan = await planner.PlanAsync(header, record, token);
        if (plan.IsFailure)
        {
            return Failed(job, watch, plan.Error.Code, plan.Error.Message);
        }

        var knob = ReplayKnobCatalogue.Check(job.KnobKind, job.KnobName, double.Parse(job.CandidateValue, CultureInfo.InvariantCulture), plan.Value.Baseline.Exit, plan.Value.Baseline.Fills.Slippage);
        if (knob.IsFailure)
        {
            return Failed(job, watch, knob.Error.Code, knob.Error.Message);
        }

        // replay: the existing handler; the counter feeds the watcher
        watch.Stage = Replay;
        watch.Done = 0;
        var counter = new Progress<int>(done => watch.Done = done);
        var started = time.GetTimestamp();
        var result = await simulation.HandleAsync(new RunSimulationQuery(plan.Value.CandidateRequest(knob.Value), counter), token);
        var replayMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsFailure)
        {
            return Failed(job, watch, result.Error.Code, result.Error.Message, replayMs);
        }

        var run = result.Value;
        if (!run.Universe.Members.Select(m => m.Id).SequenceEqual(plan.Value.Baseline.InstrumentIds))
        {
            return Failed(job, watch, ExperimentJobErrors.UniverseChangedCode, "The instruments of the universe are not the ones the baseline ran on (a basket changed). Nothing was stored; not counted as a trial.", replayMs);
        }

        // the last safe point: a stop seen here stores nothing
        token.ThrowIfCancellationRequested();
        if (await jobs.IsCancelRequestedAsync(job.Id, hostStop))
        {
            throw new OperationCanceledException(token);
        }

        // store: the candidate run, permanent and insert-only, marked as the job's
        watch.Stage = Store;
        var stored = await storeRun.HandleAsync(new StoreRunCommand(run, recordWriter.Write(run), ActorOf(job.Id)), hostStop);
        if (stored.IsFailure)
        {
            return Failed(job, watch, stored.Error.Code, stored.Error.Message, replayMs);
        }

        // compare: the judge of ADR 0072 records the experiment, once, as a trial, with the owner as actor (from the session at the start)
        watch.Stage = Compare;
        var judged = await experiment.HandleAsync(new RunExperimentCommand(job.BaselineRunKey, run.RunKey, knob.Value.Knob, new ExperimentOptions(), job.HoldOutMonths, job.CreatedBy), hostStop);
        if (judged.IsFailure)
        {
            // the stored candidate run stays; no experiment row and no trial exist
            return Failed(job, watch, judged.Error.Code, judged.Error.Message, replayMs, run.RunKey);
        }

        return new ExperimentJobFinish(job.Id, ExperimentJobState.Succeeded, Compare, run.RunKey, judged.Value.Id, null, null, replayMs, judged.Value.CompareMs, watch.Heavy, time.GetUtcNow());
    }

    private ExperimentJobFinish Failed(ExperimentJob job, Watch watch, string code, string text, long? replayMs = null, string? candidateRunKey = null)
    {
        ExperimentJobLogEvents.Failed(logger, job.Id, code);
        return new ExperimentJobFinish(job.Id, ExperimentJobState.Failed, watch.Stage, candidateRunKey, null, code, text.Length > 500 ? text[..500] : text, replayMs, null, watch.Heavy, time.GetUtcNow());
    }

    private async Task WatchAsync(ExperimentJob job, Watch watch, CancellationTokenSource owner, CancellationToken stop)
    {
        var throttle = new ProgressThrottle(time, ProgressThrottle.QuarterSecond);
        var ticks = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(WatchTick, time, stop);
                ticks++;
                if (throttle.TryPass())
                {
                    var done = watch.Done;
                    var stage = watch.Stage;
                    await jobs.ReportAsync(job.Id, stage, done, watch.Total, time.GetUtcNow(), CancellationToken.None);
                    await progress.ProgressAsync(new ExperimentJobProgress(job.Id, "running", stage, done, watch.Total, null), CancellationToken.None);
                }

                if (ticks % CancelEvery == 0)
                {
                    if (await jobs.IsCancelRequestedAsync(job.Id, CancellationToken.None))
                    {
                        await owner.CancelAsync();
                    }

                    if (!watch.Heavy && await heavy.AnyOtherRunningAsync(CancellationToken.None))
                    {
                        watch.Heavy = true;
                        await jobs.MarkOtherHeavyAsync(job.Id, CancellationToken.None);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return; // stopped by the runner
            }
#pragma warning disable CA1031 // a failed look (a database hiccup, a hub that is down) must never end the job: it is logged and the next tick looks again
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ExperimentJobLogEvents.WatcherFailed(logger, job.Id, ex);
            }
        }
    }

    private async Task StopQuietlyAsync(CancellationTokenSource stop, Task watcher, Guid jobId)
    {
        try
        {
            await stop.CancelAsync();
            await watcher;
        }
#pragma warning disable CA1031 // the watcher is a convenience: its failure never stops the job from ending
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExperimentJobLogEvents.WatcherFailed(logger, jobId, ex);
        }
    }
}
