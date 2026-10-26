using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>What a new replay job is made of (spec P8-18 Part B). The request is already checked; the store writes it as it is.</summary>
public sealed record NewExperimentJob(Guid Id, string JobKey, string BaselineRunKey, string KnobKind, string KnobName, string BaselineValue, string CandidateValue, int HoldOutMonths, string Reason, string CreatedBy, int Instruments, DateTimeOffset At);

/// <summary>A stored replay job (<c>research.experiment_jobs</c>).</summary>
public sealed record ExperimentJob(
    Guid Id,
    string JobKey,
    string BaselineRunKey,
    string KnobKind,
    string KnobName,
    string BaselineValue,
    string CandidateValue,
    int HoldOutMonths,
    string Reason,
    ExperimentJobState State,
    string? Stage,
    int Done,
    int Total,
    bool CancelRequested,
    string? CandidateRunKey,
    long? ExperimentId,
    string? ErrorCode,
    string? ErrorText,
    int Instruments,
    long? ReplayMs,
    long? CompareMs,
    bool OtherHeavyJobsSeen,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Host,
    DateTimeOffset UpdatedAt);

/// <summary>What inserting a job did.</summary>
public enum ExperimentJobInsertStatus
{
    /// <summary>A new queued job was written.</summary>
    Created,

    /// <summary>The identical input is queued, running or already succeeded: that job is returned, nothing is written.</summary>
    Existing,

    /// <summary>Another job is queued or running (one at a time): that job is returned.</summary>
    Active,

    /// <summary>The jobs of the last 24 hours are at the daily limit.</summary>
    DailyLimit,
}

/// <summary>The outcome of inserting a job.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Job">The job written or the one in the way; null for the daily limit.</param>
/// <param name="OldestLeavesAt">For the daily limit: when the oldest job of the window leaves it.</param>
public sealed record ExperimentJobInsert(ExperimentJobInsertStatus Status, ExperimentJob? Job, DateTimeOffset? OldestLeavesAt);

/// <summary>How a job ends.</summary>
public sealed record ExperimentJobFinish(Guid Id, ExperimentJobState State, string? Stage, string? CandidateRunKey, long? ExperimentId, string? ErrorCode, string? ErrorText, long? ReplayMs, long? CompareMs, bool OtherHeavyJobsSeen, DateTimeOffset At);

/// <summary>What a cancel did.</summary>
public enum ExperimentJobCancelOutcome
{
    NotFound,

    /// <summary>A queued job was cancelled at once.</summary>
    Cancelled,

    /// <summary>A running job was asked to stop at its next safe point.</summary>
    Requested,

    /// <summary>The job is final already.</summary>
    NotCancellable,
}

/// <summary>
/// Where replay jobs are kept and queued (spec P8-18 Part B, ADR 0129): <c>research.experiment_jobs</c> behind a port. The table is the queue and the daily-limit counter. The runtime role may insert a job and update only its state columns; it never deletes, and the database
/// refuses to insert one while the capability is off and to change a final state.
/// </summary>
public interface IExperimentJobStore
{
    /// <summary>
    /// Under one lock: the identical input already queued, running or succeeded returns that job; another active job is <c>Active</c>; at <paramref name="maxPerDay"/> jobs in the last <paramref name="window"/> (failed and cancelled count) is <c>DailyLimit</c>;
    /// else the job is inserted queued.
    /// </summary>
    ValueTask<ExperimentJobInsert> InsertAsync(NewExperimentJob job, int maxPerDay, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>One job, or null.</summary>
    ValueTask<ExperimentJob?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Jobs newest first.</summary>
    ValueTask<IReadOnlyList<ExperimentJob>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>How many jobs were created at or after <paramref name="since"/>, whatever their state.</summary>
    ValueTask<int> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>The newest succeeded job with a measured replay time, or null.</summary>
    ValueTask<ExperimentJob?> LatestMeasuredAsync(CancellationToken cancellationToken);

    /// <summary>The queued or running job, or null.</summary>
    ValueTask<ExperimentJob?> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>Takes the oldest queued job for <paramref name="host"/> and makes it running (stage <c>prepare</c>), or null.</summary>
    ValueTask<ExperimentJob?> ClaimNextAsync(string host, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Records the stage and the progress of a running job; false when it is not running.</summary>
    ValueTask<bool> ReportAsync(Guid id, string stage, int done, int total, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Notes that another heavy job ran while this one did.</summary>
    ValueTask MarkOtherHeavyAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Ends a running job; false when it was not running.</summary>
    ValueTask<bool> FinishAsync(ExperimentJobFinish finish, CancellationToken cancellationToken);

    /// <summary>Cancels a queued job at once, or asks a running one to stop at its next safe point.</summary>
    ValueTask<ExperimentJobCancelOutcome> RequestCancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>True when a stop was asked for this job.</summary>
    ValueTask<bool> IsCancelRequestedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Makes every job that <paramref name="host"/> had running <c>interrupted</c>; returns their ids.</summary>
    ValueTask<IReadOnlyList<Guid>> InterruptRunningAsync(string host, DateTimeOffset now, CancellationToken cancellationToken);
}
