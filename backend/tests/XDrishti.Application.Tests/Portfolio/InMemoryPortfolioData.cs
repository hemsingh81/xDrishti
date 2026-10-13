using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>
/// One in-memory stand-in for the holdings persistence ports (holdings, sync records, queries, unit of work); entities are shared by reference like a
/// tracked DbContext. Account and bucket names come from the accounts data.
/// </summary>
internal sealed class InMemoryPortfolioData(InMemoryAccountData accounts) : IHoldingStore, IHoldingQueries, IHoldingSyncStore, IPortfolioUnitOfWork
{
    public List<Holding> Holdings { get; } = [];

    public List<HoldingSync> Syncs { get; } = [];

    public int SaveCount { get; private set; }

    public int PruneCalls { get; private set; }

    /// <summary>Number of upcoming <see cref="TrySaveAsync"/> calls that report a collision with a concurrent change.</summary>
    public int CollidingSaves { get; set; }

    /// <summary>Completions made inside a transaction wait here and are applied on commit, discarded on rollback (like the update they stand for).</summary>
    private List<Action>? _pending;

    /// <summary>Set to make the next write transaction fail the way a database failure would (it throws after the work has been done).</summary>
    public Exception? FailTransactionWith { get; set; }

    /// <summary>When true another sync of every account is running: starting one is refused.</summary>
    public bool OtherRunInProgress { get; set; }

    public ValueTask<bool> AnyForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Holdings.Any(h => h.AccountId == accountId));

    public ValueTask<IReadOnlyList<Holding>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Holding>>([.. Holdings.Where(h => h.AccountId == accountId).OrderBy(h => h.FirstSeenAt).ThenBy(h => h.Id)]);

    public ValueTask<Holding?> FindAsync(Guid holdingId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Holdings.FirstOrDefault(h => h.Id == holdingId));

    public void Add(Holding holding) => Holdings.Add(holding);

    public ValueTask<IReadOnlyList<HoldingData>> ListAsync(HoldingFilter filter, CancellationToken cancellationToken)
    {
        var rows = Holdings.Select(Read).Where(x => x is not null).Select(x => x!.Value).Where(x => x.Account.ArchivedAt is null);
        rows = filter.AccountId is { } id ? rows.Where(x => x.Data.AccountId == id) : rows.Where(x => x.Account.Portfolio.Enabled && (!filter.TotalsOnly || x.Account.Portfolio.IncludeInTotals));
        if (filter.BucketId is { } bucket)
        {
            rows = rows.Where(x => x.Data.BucketId == bucket);
        }

        if (filter.Status is { } status)
        {
            rows = rows.Where(x => x.Data.Status == status);
        }

        if (filter.Text is { } text)
        {
            rows = rows.Where(x => x.Data.TradingSymbol.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Data.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (x.Data.Isin?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return ValueTask.FromResult<IReadOnlyList<HoldingData>>([.. rows.Select(x => x.Data)]);
    }

    public ValueTask<HoldingData?> GetAsync(Guid holdingId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Holdings.Where(h => h.Id == holdingId).Select(Read).FirstOrDefault(x => x is not null && x.Value.Account.ArchivedAt is null)?.Data);

    public ValueTask<bool> TryStartAsync(HoldingSync run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var stale in Syncs.Where(s => s.AccountId == run.AccountId && s.IsRunning && s.StartedAt < now - staleAfter))
        {
            stale.Fail(now, "The sync did not finish (the application stopped or crashed while it ran).");
        }

        if (OtherRunInProgress || Syncs.Any(s => s.AccountId == run.AccountId && s.IsRunning))
        {
            return ValueTask.FromResult(false);
        }

        Syncs.Add(run);
        return ValueTask.FromResult(true);
    }

    public ValueTask CompleteAsync(Guid id, SyncStatus status, SyncCounts counts, string? error, DateTimeOffset finishedAt, CancellationToken cancellationToken)
    {
        void Apply()
        {
            var run = Syncs.Single(s => s.Id == id);
            if (!run.IsRunning)
            {
                return;
            }

            if (status == SyncStatus.Succeeded)
            {
                run.Succeed(finishedAt, counts);
            }
            else if (status == SyncStatus.Skipped)
            {
                run.Skip(finishedAt, error ?? string.Empty);
            }
            else
            {
                run.Fail(finishedAt, error ?? string.Empty);
            }
        }

        if (_pending is null)
        {
            Apply();
        }
        else
        {
            _pending.Add(Apply);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DiscardAsync(Guid id, CancellationToken cancellationToken)
    {
        Syncs.RemoveAll(s => s.Id == id && s.IsRunning);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteOtherSkippedAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, Guid exceptId, CancellationToken cancellationToken)
    {
        Syncs.RemoveAll(s => s.AccountId == accountId && s.Id != exceptId && s.Status == SyncStatus.Skipped && s.Trigger == SyncTrigger.Auto && s.StartedAt >= from && s.StartedAt < to);
        return ValueTask.CompletedTask;
    }

    public ValueTask RecordSkippedAsync(HoldingSync run, CancellationToken cancellationToken)
    {
        Syncs.Add(run);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> HasSkippedBetweenAsync(Guid accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Syncs.Any(s => s.AccountId == accountId && s.Status == SyncStatus.Skipped && s.Trigger == SyncTrigger.Auto && s.StartedAt >= from && s.StartedAt < to));

    public ValueTask<int> PruneAsync(DateTimeOffset olderThan, int batch, CancellationToken cancellationToken)
    {
        PruneCalls++;
        var old = Syncs.Where(s => s.StartedAt < olderThan && s.Status is SyncStatus.Succeeded or SyncStatus.Skipped).OrderBy(s => s.StartedAt).Take(batch).ToList();
        Syncs.RemoveAll(old.Contains);
        return ValueTask.FromResult(old.Count);
    }

    public ValueTask<HoldingSync?> GetLastAsync(Guid? accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Syncs
            .Where(s => accountId is null
                ? accounts.Accounts.Any(a => a.Id == s.AccountId && a.ArchivedAt is null && a.Portfolio.Enabled && a.Portfolio.IncludeInTotals)
                : s.AccountId == accountId).OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id).FirstOrDefault());

    ValueTask<HoldingSync?> IHoldingSyncStore.GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Syncs.FirstOrDefault(s => s.Id == id));

    public ValueTask<bool> IsRunningAsync(Guid accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Syncs.Any(s => s.AccountId == accountId && s.IsRunning));

    public ValueTask<IReadOnlyDictionary<Guid, SyncActivity>> ActivityAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, SyncActivity>>(Syncs
            .Where(s => accountIds.Contains(s.AccountId))
            .GroupBy(s => s.AccountId)
            .ToDictionary(
                g => g.Key,
                g => new SyncActivity(
                    g.Where(s => s.Trigger == SyncTrigger.Auto || (s.Trigger == SyncTrigger.Import && s.RequestedBy == PortfolioRefreshRunner.Requester))
                        .Select(s => (DateTimeOffset?)s.StartedAt).Max())));

    public ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var interrupted = Syncs.Where(s => s.IsRunning && s.StartedAt < processStartedAt).ToList();
        foreach (var run in interrupted)
        {
            run.Fail(now, "The sync did not finish (the application stopped or crashed while it ran).");
        }

        return ValueTask.FromResult(interrupted.Count);
    }

    public ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        if (CollidingSaves > 0)
        {
            CollidingSaves--;
            return ValueTask.FromResult(false);
        }

        SaveCount++;
        return ValueTask.FromResult(true);
    }

    public async ValueTask<Result> RunAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken)
    {
        _pending = [];
        try
        {
            var result = await work(cancellationToken);
            if (FailTransactionWith is { } failure)
            {
                FailTransactionWith = null;
                throw failure;
            }

            if (result.IsSuccess)
            {
                foreach (var apply in _pending)
                {
                    apply();
                }
            }

            return result;
        }
        finally
        {
            _pending = null;
        }
    }

    private (HoldingData Data, Domain.Accounts.BrokerAccount Account)? Read(Holding h)
    {
        var account = accounts.Accounts.FirstOrDefault(a => a.Id == h.AccountId);
        if (account is null)
        {
            return null;
        }

        var bucket = accounts.Buckets.FirstOrDefault(b => b.Id == h.BucketId);
        var lots = h.Lots.Select(l => new LotData(
            l.Id, l.AcquiredOn, l.Qty, l.UnitCost, l.Source.ToString().ToLowerInvariant(), l.UsesDefaultDate, l.ClosedOn, l.ClosePrice, l.Estimated)).ToList();
        return (new HoldingData(
            h.Id, h.AccountId, account.Name, h.BucketId, bucket?.Name ?? "?", h.InstrumentId, h.InstrumentId is null ? null : h.TradingSymbol, h.Exchange, h.SecurityId,
            h.Isin, h.TradingSymbol, h.Name, h.Qty, h.AvgCost, h.LastPrice, h.LastPriceAt, h.Status.ToString().ToLowerInvariant(), h.FirstSeenAt, h.ClosedAt, h.SyncedAt, lots, h.PreviousClose, h.LastPriceSource.ToString().ToLowerInvariant()), account);
    }
}
