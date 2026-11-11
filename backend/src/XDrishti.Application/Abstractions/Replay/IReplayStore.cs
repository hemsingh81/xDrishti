namespace XDrishti.Application.Abstractions.Replay;

/// <summary>
/// The write side of the system replay's own tables (SR-01 part B, ADR 0272): <c>research.replay_runs</c>, <c>research.replay_sessions</c> and <c>research.replay_trades</c> and nothing else. Insert-only except the run's state, cursor, result hash, end time and failure reason.
/// A replay writes no planning, paper, shadow, learning, model or settings table.
/// </summary>
public interface IReplayStore
{
    /// <summary>Stores the runs of one request (one run, or the frozen and learning runs that share <paramref name="pairId"/>) as <c>queued</c>, in one transaction, unless a replay is queued, running or cancelling: then nothing is stored and the active runs come back.</summary>
    ValueTask<ReplayInsert> InsertAsync(Guid? pairId, IReadOnlyList<NewReplayRun> runs, CancellationToken cancellationToken);

    /// <summary>Takes the oldest queued run and makes it <c>running</c>; null when none is queued or a run is running already.</summary>
    ValueTask<ReplayRunRow?> ClaimAsync(CancellationToken cancellationToken);

    /// <summary>After a restart: a run left <c>running</c> goes back to <c>queued</c> (its cursor stays, so it resumes at the next session) and a run left <c>cancelling</c> becomes <c>cancelled</c>. Returns how many runs were touched.</summary>
    ValueTask<int> RecoverAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Stores one session of a running (or cancelling) run, with the trades that closed by it, and moves the cursor to it, in one transaction. Idempotent by (run, session): a session that is stored already is left as it is and the call returns false.
    /// </summary>
    ValueTask<bool> SaveSessionAsync(Guid runId, ReplaySessionRow session, IReadOnlyList<ReplayTradeRow> trades, CancellationToken cancellationToken);

    /// <summary>The state of the run (to see a cancel between two sessions); null when there is no such run.</summary>
    ValueTask<string?> StateAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>The hash of every stored session of the run, by session.</summary>
    ValueTask<IReadOnlyDictionary<DateOnly, string>> SessionHashesAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Ends a running or cancelling run as <c>completed</c> (with its result hash), <c>failed</c> (with its reason) or <c>cancelled</c>, storing the trades still open. False when the run was not active.</summary>
    ValueTask<bool> FinishAsync(Guid runId, string state, string? resultHash, string? failReason, IReadOnlyList<ReplayTradeRow> openTrades, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Asks a run to stop after the session in flight: a queued run becomes <c>cancelled</c> at once, a running one <c>cancelling</c>. Returns the new state, or null when there is no active run with the id.</summary>
    ValueTask<string?> CancelAsync(Guid runId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>The read side of the system replay (SR-01 part B; part C's endpoints sit on it). Read only; nothing here changes a row.</summary>
public interface IReplayReads
{
    /// <summary>One run with its completeness (sessions done of total, the as-of session) and its trial number; null when there is none.</summary>
    ValueTask<ReplayRunRow?> GetRunAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>The runs, newest first.</summary>
    ValueTask<IReadOnlyList<ReplayRunRow>> ListRunsAsync(int skip, int take, CancellationToken cancellationToken);

    /// <summary>The runs of one pair (the frozen and the learning run of a request), oldest first.</summary>
    ValueTask<IReadOnlyList<ReplayRunRow>> GetPairAsync(Guid pairId, CancellationToken cancellationToken);

    /// <summary>The stored sessions with their session between <paramref name="from"/> and <paramref name="to"/> (each end optional), oldest first.</summary>
    ValueTask<IReadOnlyList<ReplaySessionRow>> SessionsAsync(Guid runId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    /// <summary>One stored session; null when it is not stored (yet).</summary>
    ValueTask<ReplaySessionRow?> SessionAsync(Guid runId, DateOnly session, CancellationToken cancellationToken);

    /// <summary>The stored trades of the run, in booking order.</summary>
    ValueTask<IReadOnlyList<ReplayTradeRow>> TradesAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>The run and the figures of <c>PaperLedger</c> over its stored trades at its as-of session; null when there is no such run. A run with no session stored yet has no figures: null.</summary>
    ValueTask<ReplaySummary?> SummaryAsync(Guid runId, CancellationToken cancellationToken);
}

/// <summary>A hash of the bars a run reads, taken at the start so the input hash covers them (SR-01 part B). Read only, through the hold-out clamp.</summary>
public interface IReplayBarFingerprint
{
    /// <summary>The hash of the day bars of the instruments from the lookback before <paramref name="from"/> up to the close of <paramref name="to"/>.</summary>
    ValueTask<string> BarHashAsync(IReadOnlyList<Guid> instrumentIds, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
