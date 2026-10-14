using System.Reflection;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>
/// The broker history runs, watermarks and ledger entries in memory (ADR 0025), with the rules that matter for the use cases: one running run per account, none while a holdings sync of the account runs (and the other way round, which the real store
/// enforces with the shared advisory lock), a watermark that only moves forward. Entities are shared by reference like a tracked context.
/// </summary>
internal sealed class InMemoryBrokerData(InMemoryPortfolioData portfolio) : IBrokerSyncStore, ILedgerStore
{
    public List<BrokerSyncRun> Runs { get; } = [];

    public Dictionary<(Guid Account, BrokerSyncKind Kind), DateOnly> Watermarks { get; } = [];

    public List<LedgerEntry> Entries { get; } = [];

    /// <summary>Counts how many times the watermark of a kind moved forward.</summary>
    public List<(BrokerSyncKind Kind, DateOnly Date)> WatermarkMoves { get; } = [];

    /// <summary>Set to make reading a watermark throw (the database is down): a failure outside the window loop.</summary>
    public Exception? FailWatermarkReadWith { get; set; }

    /// <summary>Set to make reading a run throw (the database is down).</summary>
    public Exception? FailReadsWith { get; set; }

    /// <summary>When true another sync of every account is running: starting one is refused.</summary>
    public bool OtherRunInProgress { get; set; }

    /// <summary>Runs when a start is asked for, before it is decided: a test lets the world change at that moment (a holdings sync starting).</summary>
    public Action<BrokerSyncRun>? OnTryStart { get; set; }

    public ValueTask<bool> TryStartAsync(BrokerSyncRun run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        OnTryStart?.Invoke(run);
        foreach (var stale in Runs.Where(r => r.AccountId == run.AccountId && r.IsRunning && r.StartedAt < now - staleAfter))
        {
            End(stale, SyncStatus.Failed, now, "The sync did not finish (the application stopped or crashed while it ran).", null);
        }

        if (OtherRunInProgress || Runs.Any(r => r.AccountId == run.AccountId && r.IsRunning) || portfolio.Syncs.Any(s => s.AccountId == run.AccountId && s.IsRunning))
        {
            return ValueTask.FromResult(false);
        }

        Runs.Add(run);
        return ValueTask.FromResult(true);
    }

    public ValueTask RecordSkippedAsync(BrokerSyncRun run, CancellationToken cancellationToken)
    {
        Runs.Add(run);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateProgressAsync(Guid runId, BrokerSyncCounts counts, CancellationToken cancellationToken)
    {
        var run = Runs.Single(r => r.Id == runId);
        if (run.IsRunning)
        {
            Set(run, nameof(BrokerSyncRun.Counts), counts);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Runs when a run is about to be completed; a test returns an exception to make that fail (the database went away).</summary>
    public Func<BrokerSyncRun, Exception?>? BeforeComplete { get; set; }

    public ValueTask CompleteAsync(Guid runId, SyncStatus status, BrokerSyncCounts counts, string? error, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        var run = Runs.Single(r => r.Id == runId);
        if (BeforeComplete?.Invoke(run) is { } failure)
        {
            throw failure;
        }

        if (!run.IsRunning)
        {
            return ValueTask.CompletedTask;
        }

        End(run, status, finishedAt, error, counts);
        return ValueTask.CompletedTask;
    }

    public ValueTask<DateOnly?> GetWatermarkAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken)
    {
        if (FailWatermarkReadWith is { } failure)
        {
            throw failure;
        }

        return ValueTask.FromResult<DateOnly?>(Watermarks.TryGetValue((accountId, kind), out var date) ? date : null);
    }

    public ValueTask MoveWatermarkAsync(Guid accountId, BrokerSyncKind kind, DateOnly from, DateOnly to, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The rule of the real store: no hole after the watermark (a first read from a chosen date has no watermark yet).
        var known = Watermarks.TryGetValue((accountId, kind), out var current);
        if (known && from > current.AddDays(1))
        {
            return ValueTask.CompletedTask;
        }

        if (!known || to > current)
        {
            Watermarks[(accountId, kind)] = to;
            WatermarkMoves.Add((kind, to));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<BrokerSyncRun?> GetLastAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Runs.Where(r => r.AccountId == accountId && r.Kind == kind).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefault());

    public ValueTask<BrokerSyncRun?> GetLastSucceededAsync(Guid accountId, BrokerSyncKind kind, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Runs.Where(r => r.AccountId == accountId && r.Kind == kind && r.Status == SyncStatus.Succeeded).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefault());

    public ValueTask<BrokerSyncRun?> GetPreviousFinishedAsync(Guid accountId, BrokerSyncKind kind, Guid excludedRunId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Runs
            .Where(r => r.AccountId == accountId && r.Kind == kind && r.Id != excludedRunId && r.Status is SyncStatus.Succeeded or SyncStatus.Failed)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefault());

    public ValueTask<BrokerSyncRun?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        FailReadsWith is { } failure ? throw failure : ValueTask.FromResult(Runs.FirstOrDefault(r => r.Id == id));

    public ValueTask<bool> IsRunningAsync(Guid accountId, CancellationToken cancellationToken) => ValueTask.FromResult(Runs.Any(r => r.AccountId == accountId && r.IsRunning));

    public ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var interrupted = Runs.Where(r => r.IsRunning && r.StartedAt < processStartedAt).ToList();
        foreach (var run in interrupted)
        {
            End(run, SyncStatus.Failed, now, "The sync did not finish (the application stopped or crashed while it ran).", null);
        }

        return ValueTask.FromResult(interrupted.Count);
    }

    public ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<string>>(Entries.Where(e => e.AccountId == accountId && externalIds.Contains(e.ExternalId)).Select(e => e.ExternalId).ToHashSet(StringComparer.Ordinal));

    public void Add(IEnumerable<LedgerEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (Entries.Any(e => e.AccountId == entry.AccountId && e.ExternalId == entry.ExternalId))
            {
                throw new InvalidOperationException("The unique index would refuse this entry.");
            }

            Entries.Add(entry);
        }
    }

    public ValueTask<(IReadOnlyList<LedgerEntry> Items, int Total)> ListAsync(Guid accountId, DateOnly? from, DateOnly? to, LedgerClass? entryClass, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = Filter(accountId, from, to).Where(e => entryClass is null || e.EntryClass == entryClass).OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).ToList();
        return ValueTask.FromResult<(IReadOnlyList<LedgerEntry>, int)>(([.. all.Skip((page - 1) * pageSize).Take(pageSize)], all.Count));
    }

    public ValueTask<IReadOnlyList<LedgerTotal>> TotalsAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<LedgerTotal>>([.. Filter(accountId, from, to).GroupBy(e => e.EntryClass).Select(g => new LedgerTotal(g.Key, g.Count(), g.Sum(e => e.Amount)))]);

    private IEnumerable<LedgerEntry> Filter(Guid accountId, DateOnly? from, DateOnly? to) =>
        Entries.Where(e => e.AccountId == accountId && (from is null || e.EntryDate >= from) && (to is null || e.EntryDate <= to));

    /// <summary>The real store ends a run with a guarded UPDATE; the entity has no methods for it, so the double sets the same columns.</summary>
    private static void End(BrokerSyncRun run, SyncStatus status, DateTimeOffset finishedAt, string? error, BrokerSyncCounts? counts)
    {
        Set(run, nameof(BrokerSyncRun.Status), status);
        Set(run, nameof(BrokerSyncRun.FinishedAt), finishedAt);
        Set(run, nameof(BrokerSyncRun.Error), error is { Length: > BrokerSyncRun.MaxErrorLength } ? error[..BrokerSyncRun.MaxErrorLength] : error);
        if (counts is not null)
        {
            Set(run, nameof(BrokerSyncRun.Counts), counts);
        }
    }

    private static void Set(BrokerSyncRun run, string property, object? value) =>
        typeof(BrokerSyncRun).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.GetSetMethod(nonPublic: true)!.Invoke(run, [value]);
}
