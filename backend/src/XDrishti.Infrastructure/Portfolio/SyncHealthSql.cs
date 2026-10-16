namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The statements that keep the health figures of the syncs in <c>portfolio.broker_sync_state</c> (ADR 0027), shared by every place that fails a run: the health store, the stores that close stale and interrupted runs, so the rule is written once. Parameters: <c>@account</c>,
/// <c>@kind</c>, <c>@reason</c>, <c>@now</c>. The India day of a time is its date in <c>Asia/Kolkata</c>: the first failure of a new day adds one to the days in a row and restarts the day's attempts, a later failure of the same day only adds an attempt.
/// </summary>
internal static class SyncHealthSql
{
    /// <summary>The <c>SET</c> of the update of a state row (aliased <c>s</c>) for a failure at <c>@now</c> for <c>@reason</c>.</summary>
    public const string FailureSet = """
        consecutive_failed_days = CASE WHEN s.last_failed_at IS NULL OR (s.last_failed_at AT TIME ZONE 'Asia/Kolkata')::date < (@now AT TIME ZONE 'Asia/Kolkata')::date THEN s.consecutive_failed_days + 1 ELSE s.consecutive_failed_days END,
        attempts = CASE WHEN s.last_failed_at IS NOT NULL AND (s.last_failed_at AT TIME ZONE 'Asia/Kolkata')::date = (@now AT TIME ZONE 'Asia/Kolkata')::date THEN s.attempts + 1 ELSE 1 END,
        last_failure_reason = @reason,
        last_failed_at = @now,
        updated_at = @now
        """;

    /// <summary>Notes a failure of one account and kind; creates the row.</summary>
    public const string RecordFailure = $"""
        INSERT INTO portfolio.broker_sync_state AS s (account_id, kind, consecutive_failed_days, last_failure_reason, last_failed_at, attempts, updated_at)
        VALUES (@account, @kind, 1, @reason, @now, 1, @now)
        ON CONFLICT (account_id, kind) DO UPDATE SET {FailureSet}
        """;

    /// <summary>Clears the failure counters of one account and kind; touches nothing while it is healthy.</summary>
    public const string RecordSuccess = """
        UPDATE portfolio.broker_sync_state
        SET consecutive_failed_days = 0, last_failure_reason = NULL, last_failed_at = NULL, attempts = 0, updated_at = @now
        WHERE account_id = @account AND kind = @kind AND (consecutive_failed_days <> 0 OR last_failure_reason IS NOT NULL OR attempts <> 0)
        """;

    /// <summary>
    /// Fails the runs of <paramref name="runTable"/> that are still running and match <paramref name="where"/> (parameters of its own, plus <c>@now</c>, <c>@error</c> and <c>@reason</c> = <c>interrupted</c>), notes the interruption for each account and kind in the same statement and
    /// answers how many runs it failed. <paramref name="kindColumn"/> is the column that names the kind or a literal (<c>'holdings'</c>).
    /// </summary>
    public static string FailRunning(string runTable, string kindColumn, string where) => $"""
        WITH failed AS (
            UPDATE {runTable} SET status = 'failed', finished_at = @now, error = @error
            WHERE status = 'running' AND {where}
            RETURNING account_id, {kindColumn} AS kind),
        noted AS (
            INSERT INTO portfolio.broker_sync_state AS s (account_id, kind, consecutive_failed_days, last_failure_reason, last_failed_at, attempts, updated_at)
            SELECT DISTINCT account_id, kind, 1, @reason, @now, 1, @now FROM failed
            ON CONFLICT (account_id, kind) DO UPDATE SET {FailureSet}
            RETURNING 1)
        SELECT count(*) FROM failed
        """;
}
