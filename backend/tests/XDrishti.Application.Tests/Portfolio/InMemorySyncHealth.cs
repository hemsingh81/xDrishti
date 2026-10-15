using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>
/// The health figures of the syncs and the noted windows in memory (ADR 0027), with the rules of the real store: the first failure of a new India day adds a failed day and restarts the day's attempts, a later one of the same day adds an attempt, a success clears the counters and
/// touches nothing while healthy, the mapper version never goes down, windows are cleared by a read that covers them.
/// </summary>
internal sealed class InMemorySyncHealth : ISyncHealthStore
{
    public Dictionary<(Guid Account, SyncHealthKind Kind), SyncHealth> Rows { get; } = [];

    /// <summary>The windows noted as dropped, with whether they still need a new read.</summary>
    public List<(Guid Account, SyncHealthKind Kind, DateOnly From, DateOnly To, int Dropped, bool NeedsReread)> Windows { get; } = [];

    /// <summary>Set to make writing the health figures fail (the database is down): the sync that ended must not fail because of it. The notes of the windows are part of the window's own unit of work and are not affected.</summary>
    public Exception? FailWritesWith { get; set; }

    public SyncHealth Of(Guid accountId, SyncHealthKind kind) => Rows.GetValueOrDefault((accountId, kind)) ?? SyncHealth.None(accountId, kind);

    public ValueTask<SyncHealth> GetAsync(Guid accountId, SyncHealthKind kind, CancellationToken cancellationToken) => ValueTask.FromResult(Of(accountId, kind));

    public ValueTask<IReadOnlyList<SyncHealth>> ListAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<SyncHealth>>([.. Rows.Values.Where(r => accountIds.Contains(r.AccountId))]);

    public ValueTask RecordFailureAsync(Guid accountId, SyncHealthKind kind, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Write();
        var row = Of(accountId, kind);
        var newDay = row.LastFailedAt is not { } last || IndiaTime.Today(last) < IndiaTime.Today(now);
        Rows[(accountId, kind)] = row with
        {
            ConsecutiveFailedDays = row.ConsecutiveFailedDays + (newDay ? 1 : 0),
            Attempts = newDay ? 1 : row.Attempts + 1,
            LastFailureReason = reason,
            LastFailedAt = now,
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask RecordSuccessAsync(Guid accountId, SyncHealthKind kind, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Write();
        if (Rows.TryGetValue((accountId, kind), out var row) && (row.ConsecutiveFailedDays != 0 || row.LastFailureReason is not null || row.Attempts != 0))
        {
            Rows[(accountId, kind)] = row with { ConsecutiveFailedDays = 0, LastFailureReason = null, LastFailedAt = null, Attempts = 0 };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StampMapperVersionAsync(Guid accountId, SyncHealthKind kind, int version, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Write();
        var row = Of(accountId, kind);
        if (row.MapperVersion < version)
        {
            Rows[(accountId, kind)] = row with { MapperVersion = version };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordDeepenedAsync(Guid accountId, DateOnly? deepenedTo, bool exhausted, DateOnly on, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Write();
        var row = Of(accountId, SyncHealthKind.Trades);
        var reached = (row.DeepenedTo, deepenedTo) switch
        {
            ({ } a, { } b) => a < b ? a : b,
            ({ } a, null) => a,
            (null, var b) => b,
        };
        Rows[(accountId, SyncHealthKind.Trades)] = row with { DeepenedTo = reached, DeepenExhausted = row.DeepenExhausted || exhausted, DeepenedOn = on };
        return ValueTask.CompletedTask;
    }

    public ValueTask NoteWindowAsync(Guid accountId, SyncHealthKind kind, DateOnly from, DateOnly to, int dropped, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var i = 0; i < Windows.Count; i++)
        {
            var w = Windows[i];
            if (w.Account == accountId && w.Kind == kind && w.NeedsReread && w.From >= from && w.To <= to)
            {
                Windows[i] = w with { NeedsReread = false };
            }
        }

        if (dropped > 0)
        {
            Windows.RemoveAll(w => w.Account == accountId && w.Kind == kind && w.From == from);
            Windows.Add((accountId, kind, from, to, dropped, true));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<BrokerDateRange>> ListRereadAsync(Guid accountId, SyncHealthKind kind, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BrokerDateRange>>([.. Windows.Where(w => w.Account == accountId && w.Kind == kind && w.NeedsReread).OrderBy(w => w.From).Select(w => new BrokerDateRange(w.From, w.To))]);

    /// <summary>The runs the stores failed as interrupted note it too (the real ones do it in the same statement).</summary>
    public void Interrupted(Guid accountId, SyncHealthKind kind, DateTimeOffset now) => RecordFailureAsync(accountId, kind, "interrupted", now, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    private void Write()
    {
        if (FailWritesWith is { } failure)
        {
            throw failure;
        }
    }
}
