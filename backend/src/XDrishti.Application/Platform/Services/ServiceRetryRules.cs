using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// When a failed attempt belongs to a sequence of retries that is still going (ADR 0018 §3): the attempt failed (not by a timeout, not because the application stopped), the service's retries allow another one,
/// and not too long has passed (the longest back-off is one hour). The scheduler resumes such a sequence after a restart; the failure notice waits until the sequence is over.
/// </summary>
internal static class ServiceRetryRules
{
    /// <summary>A failed attempt older than this is not resumed after a restart and counts as the end of its sequence (the longest back-off is one hour).</summary>
    public static readonly TimeSpan ResumeWindow = TimeSpan.FromHours(2);

    /// <summary>The failed run may be followed by another attempt: it failed, was not timed out or interrupted, and <c>retries</c> allow one more.</summary>
    public static bool MayContinue(ServiceRun run, int retries) =>
        run is { Status: ServiceRunStatus.Failed, IsTimedOut: false, IsInterrupted: false } && run.Attempt <= Math.Min(retries, ServiceSettings.MaxRetries);

    /// <summary>The sequence the failed run belongs to is over: no attempt can follow it, the service is paused, or the next attempt is long overdue.</summary>
    public static bool IsOver(ServiceRun run, int retries, bool enabled, DateTimeOffset now) =>
        !MayContinue(run, retries) || !enabled || now - (run.FinishedAt ?? run.StartedAt) > ResumeWindow;

    /// <summary>When the attempt that follows the failed one is due (after the back-off).</summary>
    public static DateTimeOffset NextDue(ServiceRun failed) =>
        (failed.FinishedAt ?? failed.StartedAt) + ServiceRunner.Backoff[Math.Min(failed.Attempt, ServiceRunner.Backoff.Count) - 1];
}
