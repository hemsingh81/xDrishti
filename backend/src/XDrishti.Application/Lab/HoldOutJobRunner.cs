using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// Carries out a claimed <c>holdout</c> job (spec CF-06, part C, D5): the one look at the hold-out that the owner asked for, run on the Lab dispatcher with a stage log and progress events. The password never reaches here. A failure to look ends the job
/// <c>failed</c> with the code of the refusal; a cancel seen before the pass starts ends it <c>cancelled</c>; a stop of the host leaves it <c>running</c> for the next start to mark <c>interrupted</c>, and asking again is safe.
/// </summary>
internal sealed class HoldOutJobRunner(ILabRunStore runs, HoldOutUnlocker unlocker, ILabProgress progress, HoldOutOptions options, TimeProvider time, ILogger<HoldOutJobRunner> logger) : ILabRunExecutor
{
    public async Task ExecuteAsync(LabRun run, CancellationToken hostStop)
    {
        ArgumentNullException.ThrowIfNull(run);
        var started = time.GetUtcNow();
        LabRunState state;
        string? error = null;
        List<LabProblem> problems = [];
        try
        {
            await runs.ReportAsync(run.Id, HoldOutJob.Stage, 0, 0, [new LabStageLog(HoldOutJob.Stage, started, null, 0)], started, hostStop);
            await progress.ProgressAsync(new LabRunProgress(run.Id, LabRunStates.Code(LabRunState.Running), HoldOutJob.Stage, 0, 0, null), hostStop);
            if (HoldOutJob.Parse(run) is not { } job)
            {
                (state, error) = (LabRunState.Failed, LabErrors.HoldOutUnavailableCode);
                problems.Add(new LabProblem(HoldOutJob.Stage, "job", error, "The job does not name the run it unlocks.", "error", null, null));
            }
            else if (await runs.IsCancelRequestedAsync(run.Id, hostStop))
            {
                state = LabRunState.Cancelled;
            }
            else if (string.Equals(HoldOutJob.ModeOf(job.Actor), HoldOutUnlocker.AutoMode, StringComparison.Ordinal) && !options.AutoUnlock)
            {
                // an automatic look queued while the setting was ON is not taken after it was turned OFF
                state = LabRunState.Cancelled;
                problems.Add(new LabProblem(HoldOutJob.Stage, "holdout", "lab.holdout_auto_off", "The automatic look was queued while the setting was ON and is not taken now that it is OFF.", "info", null, null));
            }
            else if (string.Equals(HoldOutJob.ModeOf(job.Actor), HoldOutUnlocker.AutoMode, StringComparison.Ordinal) && await unlocker.AutoLookRefusalAsync(job.RunId, hostStop) is { } refusal)
            {
                // the slot was taken, or the version is not pre-dated, while the job waited: nothing is stored, so the version's look at the block is not burned as a re-used one
                state = LabRunState.Cancelled;
                problems.Add(new LabProblem(HoldOutJob.Stage, "holdout", "lab.holdout_auto_refused", "The automatic look was not taken: " + refusal + ".", "info", null, null));
            }
            else
            {
                var looked = await unlocker.UnlockAsync(job.RunId, HoldOutJob.ModeOf(job.Actor), job.Actor, hostStop);
                state = looked.IsSuccess ? LabRunState.Succeeded : LabRunState.Failed;
                if (looked.IsSuccess)
                {
                    LabRunLog.HoldOutUnlocked(logger, job.RunId, looked.Value.Mode, looked.Value.State);
                }
                else
                {
                    error = looked.Error.Code;
                    problems.Add(new LabProblem(HoldOutJob.Stage, "holdout", looked.Error.Code, looked.Error.Message, "error", null, null));
                }
            }
        }
        catch (OperationCanceledException) when (hostStop.IsCancellationRequested)
        {
            return; // the host is stopping: the job stays running and the next start marks it interrupted
        }
#pragma warning disable CA1031 // an unexpected failure is recorded on the job so its key is freed; the host goes on
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LabRunLog.Crashed(logger, run.Id, HoldOutJob.Stage, ex);
            (state, error) = (LabRunState.Failed, "lab.run_crashed");
            problems.Add(new LabProblem(HoldOutJob.Stage, "holdout", error, "The look failed unexpectedly; asking again is safe.", "error", null, null));
        }

        var finished = time.GetUtcNow();
        await runs.FinishAsync(new LabFinish(run.Id, state, HoldOutJob.Stage, [], null, problems, [new LabStageLog(HoldOutJob.Stage, started, finished, problems.Count)], error, finished), CancellationToken.None);
        await progress.FinishedAsync(new LabRunFinished(run.Id, LabRunStates.Code(state)), CancellationToken.None);
    }
}

/// <summary>The executor the dispatcher resolves: a <c>holdout</c> job goes to <see cref="HoldOutJobRunner"/>, every other run to the <see cref="LabRunner"/>.</summary>
internal sealed class LabRunRouter(LabRunner runner, HoldOutJobRunner holdOut) : ILabRunExecutor
{
    public Task ExecuteAsync(LabRun run, CancellationToken hostStop)
    {
        ArgumentNullException.ThrowIfNull(run);
        return HoldOutJob.Is(run) ? holdOut.ExecuteAsync(run, hostStop) : runner.ExecuteAsync(run, hostStop);
    }
}
