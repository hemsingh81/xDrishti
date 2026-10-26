using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>The refusals of the replay job (spec P8-18 Part B).</summary>
public static class ExperimentJobErrors
{
    public const string ActiveCode = "experiments.job_active";
    public const string DailyLimitCode = "experiments.daily_limit";
    public const string NotFoundCode = "experiments.job_not_found";
    public const string NotCancellableCode = "experiments.job_not_cancellable";
    public const string NotRebuildableCode = "experiments.baseline_not_rebuildable";
    public const string TimeoutCode = "experiments.timeout";
    public const string UniverseChangedCode = "experiments.universe_changed";
    public const string CrashedCode = "experiments.job_crashed";

    /// <summary>One job at a time; the one in the way is named.</summary>
    public static Error Active(Guid id) => Error.Conflict(ActiveCode, "Another replay is queued or running (job " + id.ToString("D") + "); one at a time. Wait for it or cancel it.");

    /// <summary>Three starts in 24 hours (failed and cancelled count).</summary>
    public static Error DailyLimit(int limit, DateTimeOffset leavesAt, int retryAfterSeconds) =>
        Error.RateLimited(DailyLimitCode, "The daily limit of " + limit.ToString(System.Globalization.CultureInfo.InvariantCulture) + " replays is reached; the oldest leaves the window at " + leavesAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture) + ".", retryAfterSeconds);

    public static Error NotFound(Guid id) => Error.NotFound(NotFoundCode, "No replay job " + id.ToString("D") + ".");

    public static Error NotCancellable => Error.Conflict(NotCancellableCode, "The job has ended already; there is nothing to cancel.");

    /// <summary>The baseline cannot be replayed from its stored record; it is refused with the reason, never guessed.</summary>
    public static Error NotRebuildable(string reason) => Error.Validation(NotRebuildableCode, "This baseline cannot be replayed from its stored record: " + reason + ".");
}
