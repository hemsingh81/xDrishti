using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Job;

/// <summary>The replay store in memory, with the rules of the SQL one (one active replay, a claim only when none runs, idempotent sessions, final runs stay final); <c>AfterSave</c> lets a test stop the "process" or cancel between two sessions.</summary>
internal sealed class FakeReplayStore : IReplayStore, IReplayReads
{
    public sealed class Run(NewReplayRun created, Guid? pair)
    {
        public NewReplayRun Created { get; } = created;

        public Guid? Pair { get; } = pair;

        public string State { get; set; } = ReplayRunStates.Queued;

        public bool Recorded { get; set; }

        public DateOnly? Cursor { get; set; }

        public string? ResultHash { get; set; }

        public string? FailReason { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public SortedDictionary<DateOnly, ReplaySessionRow> Sessions { get; } = [];

        public List<ReplayTradeRow> Trades { get; } = [];
    }

    public List<Run> Runs { get; } = [];

    public int SaveCalls { get; private set; }

    /// <summary>How many of the next saves throw (a store that fails): they store nothing, or, with <see cref="FailAfterStoring"/>, store the session and then throw as a lost connection would.</summary>
    public int FailSaves { get; set; }

    public bool FailAfterStoring { get; set; }

    public bool FailRecoverOnce { get; set; }

    /// <summary>Called after a session was stored, with the run and how many sessions it has now.</summary>
    public Action<Guid, int>? AfterSave { get; set; }

    /// <summary>When set, a cancel arrives (as the owner's request would) right after this many sessions of a run are stored.</summary>
    public int? CancelAfterSessions { get; set; }

    public Run Of(Guid id) => Runs.Single(r => r.Created.Id == id);

    private ReplayRunRow Row(Run r) => new(
        r.Created.Id, r.Pair, r.Created.From, r.Created.To, r.Created.Mode, r.Created.ParamsJson, r.Created.SettingsJson, r.Created.VersionsJson, r.Created.InputHash, r.Created.ScopeHash, r.ResultHash, r.State, r.Cursor, r.Sessions.Count, r.Created.SessionsTotal,
        Runs.Count(o => o.Created.From == r.Created.From && o.Created.To == r.Created.To && (o.Created.CreatedAt, o.Created.Id).CompareTo((r.Created.CreatedAt, r.Created.Id)) <= 0), r.Created.CreatedAt, r.FinishedAt, r.FailReason);

    public ValueTask<ReplayInsert> InsertAsync(Guid? pairId, IReadOnlyList<NewReplayRun> runs, CancellationToken cancellationToken)
    {
        if (Runs.Any(r => ReplayRunStates.IsActive(r.State)))
        {
            return ValueTask.FromResult(new ReplayInsert(false, [.. Runs.Where(r => ReplayRunStates.IsActive(r.State)).Select(Row)]));
        }

        var added = runs.Select(n => new Run(n, pairId)).ToList();
        Runs.AddRange(added);
        return ValueTask.FromResult(new ReplayInsert(true, [.. added.Select(Row)]));
    }

    public ValueTask MarkRecordedAsync(IReadOnlyList<Guid> runIds, CancellationToken cancellationToken)
    {
        foreach (var r in Runs.Where(r => runIds.Contains(r.Created.Id) && r.State == ReplayRunStates.Queued))
        {
            r.Recorded = true;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Makes every queued run recorded, as the start does after its ledger rows are written (a test that sets a run up by hand).</summary>
    public void RecordAll() => Runs.ForEach(r => r.Recorded = true);

    public ValueTask<bool> AnyRunningAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Runs.Any(r => r.State is ReplayRunStates.Running or ReplayRunStates.Cancelling));

    public ValueTask<ReplayRunRow?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (Runs.Any(r => r.State is ReplayRunStates.Running or ReplayRunStates.Cancelling) || Runs.Where(r => r.State == ReplayRunStates.Queued && r.Recorded).OrderBy(r => r.Created.CreatedAt).ThenBy(r => r.Created.Id).FirstOrDefault() is not { } next)
        {
            return ValueTask.FromResult<ReplayRunRow?>(null);
        }

        next.State = ReplayRunStates.Running;
        return ValueTask.FromResult<ReplayRunRow?>(Row(next));
    }

    public ValueTask<int> RecoverAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (FailRecoverOnce)
        {
            FailRecoverOnce = false;
            throw new InvalidOperationException("the store is down");
        }

        var touched = 0;
        foreach (var r in Runs)
        {
            if (r.State == ReplayRunStates.Running)
            {
                r.State = ReplayRunStates.Queued;
                touched++;
            }
            else if (r.State == ReplayRunStates.Cancelling || (r.State == ReplayRunStates.Queued && !r.Recorded))
            {
                r.State = ReplayRunStates.Cancelled;
                r.FinishedAt = now;
                touched++;
            }
        }

        return ValueTask.FromResult(touched);
    }

    public ValueTask<bool> SaveSessionAsync(Guid runId, ReplaySessionRow session, IReadOnlyList<ReplayTradeRow> trades, CancellationToken cancellationToken)
    {
        SaveCalls++;
        var failing = FailSaves > 0;
        if (failing)
        {
            FailSaves--;
            if (!FailAfterStoring)
            {
                throw new InvalidOperationException("the store is down");
            }
        }

        var run = Of(runId);
        if (run.State is not (ReplayRunStates.Running or ReplayRunStates.Cancelling) || run.Sessions.ContainsKey(session.Session))
        {
            return ValueTask.FromResult(false);
        }

        run.Sessions[session.Session] = session;
        run.Trades.AddRange(trades.Where(t => run.Trades.All(o => o.TradeNo != t.TradeNo)));
        run.Cursor = run.Cursor is { } c && c > session.Session ? c : session.Session;
        if (CancelAfterSessions == run.Sessions.Count)
        {
            run.State = ReplayRunStates.Cancelling;
        }

        AfterSave?.Invoke(runId, run.Sessions.Count);
        return failing ? throw new InvalidOperationException("the connection was lost after the write") : ValueTask.FromResult(true);
    }

    /// <summary>Called at every look at the state, with the run and how many looks there have been.</summary>
    public Action<Guid, int>? OnState { get; set; }

    private int _stateLooks;

    public ValueTask<string?> StateAsync(Guid runId, CancellationToken cancellationToken)
    {
        OnState?.Invoke(runId, ++_stateLooks);
        return ValueTask.FromResult(Runs.FirstOrDefault(r => r.Created.Id == runId)?.State);
    }

    public ValueTask<IReadOnlyDictionary<DateOnly, string>> SessionHashesAsync(Guid runId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<DateOnly, string>>(Of(runId).Sessions.ToDictionary(p => p.Key, p => p.Value.RecordSha256));

    public ValueTask<bool> FinishAsync(Guid runId, string state, string? resultHash, string? failReason, IReadOnlyList<ReplayTradeRow> openTrades, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = Of(runId);
        if (run.State is not (ReplayRunStates.Running or ReplayRunStates.Cancelling))
        {
            return ValueTask.FromResult(false);
        }

        run.State = state;
        run.ResultHash = resultHash;
        run.FailReason = failReason;
        run.FinishedAt = now;
        run.Trades.AddRange(openTrades.Where(t => run.Trades.All(o => o.TradeNo != t.TradeNo)));
        return ValueTask.FromResult(true);
    }

    public ValueTask<string?> CancelAsync(Guid runId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = Runs.FirstOrDefault(r => r.Created.Id == runId);
        if (run is null || !ReplayRunStates.IsActive(run.State))
        {
            return ValueTask.FromResult<string?>(null);
        }

        if (run.State == ReplayRunStates.Queued)
        {
            run.State = ReplayRunStates.Cancelled;
            run.FinishedAt = now;
        }
        else
        {
            run.State = ReplayRunStates.Cancelling;
        }

        return ValueTask.FromResult<string?>(run.State);
    }

    public ValueTask<ReplayRunRow?> GetRunAsync(Guid runId, CancellationToken cancellationToken) => ValueTask.FromResult(Runs.Where(r => r.Created.Id == runId).Select(r => (ReplayRunRow?)Row(r)).FirstOrDefault());

    public ValueTask<IReadOnlyList<ReplayRunRow>> ListRunsAsync(int skip, int take, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<ReplayRunRow>>([.. Runs.OrderByDescending(r => r.Created.CreatedAt).Skip(skip).Take(take).Select(Row)]);

    public ValueTask<IReadOnlyList<ReplayRunRow>> GetPairAsync(Guid pairId, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<ReplayRunRow>>([.. Runs.Where(r => r.Pair == pairId).Select(Row)]);

    public ValueTask<IReadOnlyList<ReplaySessionRow>> SessionsAsync(Guid runId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ReplaySessionRow>>([.. Of(runId).Sessions.Values.Where(s => (from is null || s.Session >= from) && (to is null || s.Session <= to))]);

    public async ValueTask<ReplaySessionRow?> SessionAsync(Guid runId, DateOnly session, CancellationToken cancellationToken) => (await SessionsAsync(runId, session, session, cancellationToken)).SingleOrDefault();

    public ValueTask<IReadOnlyList<ReplayTradeRow>> TradesAsync(Guid runId, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<ReplayTradeRow>>([.. Of(runId).Trades.OrderBy(t => t.TradeNo)]);

    public ValueTask<ReplaySummary?> SummaryAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException("covered by the database test of the store");
}
