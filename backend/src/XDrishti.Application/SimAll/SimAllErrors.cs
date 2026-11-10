using XDrishti.Domain.Common;

namespace XDrishti.Application.SimAll;

/// <summary>The expected failures of the simulate-everything job (spec AU-12).</summary>
public static class SimAllErrors
{
    public const string ActiveCode = "simulate_all.active";
    public const string BackfillIncompleteCode = "simulate_all.backfill_incomplete";
    public const string NothingToRunCode = "simulate_all.nothing_to_run";
    public const string OwnerOnlyCode = "simulate_all.owner_only";
    public const string NoActiveJobCode = "simulate_all.no_active_job";
    public const string NotPausedCode = "simulate_all.not_paused";
    public const string NotPausableCode = "simulate_all.not_pausable";
    public const string ChunkTimeoutCode = "simulate_all.chunk_timeout";
    public const string ChunkCrashedCode = "simulate_all.chunk_crashed";
    public const string StrategyMissingCode = "simulate_all.strategy_missing";
    public const string CompileFailedCode = "simulate_all.compile_failed";

    /// <summary>A job is running or paused: 409.</summary>
    public static Error Active(Guid id) => Error.Conflict(ActiveCode, "A simulate-everything job is active already (" + id.ToString("D") + "); pause or cancel it, or wait for it to end.");

    /// <summary>The backfill is not complete: the job does not start (fail closed), 422.</summary>
    public static Error BackfillIncomplete(string reason) => Error.Validation(BackfillIncompleteCode, "The history backfill is not complete, so nothing is simulated yet: " + reason);

    /// <summary>No strategy or no instrument to run: 422.</summary>
    public static Error NothingToRun(string reason) => Error.Validation(NothingToRunCode, reason);

    /// <summary>Only the signed-in owner starts, pauses, resumes or cancels the job: 403.</summary>
    public static Error OwnerOnly => Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner controls the simulate-everything job.");

    /// <summary>There is no active job: 404.</summary>
    public static Error NoActiveJob => Error.NotFound(NoActiveJobCode, "There is no active simulate-everything job.");

    /// <summary>The job is not paused: 409.</summary>
    public static Error NotPaused => Error.Conflict(NotPausedCode, "The job is not paused.");

    /// <summary>The job is not running: 409.</summary>
    public static Error NotPausable => Error.Conflict(NotPausableCode, "The job is not running.");
}
