namespace XDrishti.Application.Market.Bars;

/// <summary>
/// Tells that the data jobs an earlier process left "running" have been cleaned up (<see cref="RecoverInterruptedDataJobsCommand"/>). The data worker marks it done once it has recovered
/// (also when the recovery failed: a stale job expires by itself); the scheduled end-of-day run waits for it before its start-up catch-up decides, so a job that is only stuck from the
/// previous process is not mistaken for a running one and the day's run skipped.
/// </summary>
public sealed class DataJobRecovery
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the recovery has finished.</summary>
    public Task Completed => _done.Task;

    public void MarkCompleted() => _done.TrySetResult();
}
