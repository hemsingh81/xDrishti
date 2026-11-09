using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>The recovery ledger in memory, with the unique key of the table: one item per service and occurrence, updated in place.</summary>
internal sealed class InMemoryRecoveryLedger : IRecoveryLedger
{
    private readonly object _gate = new();

    public List<(Guid Id, string Trigger, DateTimeOffset StartedAt, string Status)> Passes { get; } = [];

    public Dictionary<(string Service, string Occurrence), RecoveryItemRow> Items { get; } = [];

    public ValueTask<Guid> BeginPassAsync(string trigger, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var id = Guid.CreateVersion7(now);
            Passes.Add((id, trigger, now, "running"));
            return ValueTask.FromResult(id);
        }
    }

    public ValueTask EndPassAsync(Guid passId, string status, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var i = Passes.FindIndex(p => p.Id == passId && p.Status == "running");
            if (i >= 0)
            {
                Passes[i] = Passes[i] with { Status = status };
            }

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<int> InterruptRunningAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var count = 0;
            foreach (var key in Items.Where(i => i.Value.State == RecoveryStates.Running).Select(i => i.Key).ToList())
            {
                Items[key] = Items[key] with { State = RecoveryStates.Failed, Reason = "interrupted" };
                count++;
            }

            for (var i = 0; i < Passes.Count; i++)
            {
                if (Passes[i].Status == "running")
                {
                    Passes[i] = Passes[i] with { Status = "interrupted" };
                }
            }

            return ValueTask.FromResult(count);
        }
    }

    public ValueTask<RecoveryItemRow?> FindAsync(string serviceId, string occurrence, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(Items.GetValueOrDefault((serviceId, occurrence)));
        }
    }

    public ValueTask<RecoveryItemRow?> FindNewestAsync(string serviceId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(Items.Values.Where(i => i.ServiceId == serviceId).OrderByDescending(i => i.Occurrence, StringComparer.Ordinal).FirstOrDefault());
        }
    }

    public ValueTask<IReadOnlyList<RecoveryItemRow>> ListRetryableAsync(int maxAttempts, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<RecoveryItemRow> rows = [.. Items.Values.Where(i => (i.State == RecoveryStates.Failed && i.Attempts < maxAttempts) || i.State == RecoveryStates.Waiting)];
            return ValueTask.FromResult(rows);
        }
    }

    public ValueTask SupersedeOlderAsync(string serviceId, string occurrence, int maxAttempts, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var key in Items.Where(i => i.Key.Service == serviceId && string.CompareOrdinal(i.Key.Occurrence, occurrence) < 0 && i.Value.State is RecoveryStates.Failed or RecoveryStates.Blocked or RecoveryStates.Waiting && !(i.Value.State == RecoveryStates.Failed && i.Value.Attempts >= maxAttempts)).Select(i => i.Key).ToList())
            {
                Items[key] = Items[key] with { State = RecoveryStates.Superseded, Reason = "superseded by " + occurrence };
            }

            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<IReadOnlyList<RecoveryExhaustedRow>> ListWaitingAsync(DateOnly sessionOnOrBefore, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<RecoveryExhaustedRow> rows = [.. Items.Values
                .Where(i => i.State == RecoveryStates.Waiting && i.SessionTo <= sessionOnOrBefore)
                .Select(i => new RecoveryExhaustedRow(i.ServiceId, i.Occurrence, i.Reason, Passes.FirstOrDefault(p => p.Id == i.PassId).StartedAt))];
            return ValueTask.FromResult(rows);
        }
    }

    public ValueTask SaveAsync(RecoveryItemRow item, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Items[(item.ServiceId, item.Occurrence)] = item;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<IReadOnlyList<RecoveryItemRow>> ListPartialAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<RecoveryItemRow>>([.. Items.Values.Where(i => i.State == RecoveryStates.Partial).OrderByDescending(i => i.Occurrence).Take(50)]);
        }
    }

    public ValueTask<IReadOnlyList<RecoveryExhaustedRow>> ListExhaustedAsync(int maxAttempts, DateTimeOffset since, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<RecoveryExhaustedRow> rows = [.. Items.Values
                .Where(i => i.State == RecoveryStates.Failed && i.Attempts >= maxAttempts)
                .Select(i => (Item: i, Pass: Passes.FirstOrDefault(p => p.Id == i.PassId)))
                .Where(x => x.Pass.StartedAt >= since)
                .Select(x => new RecoveryExhaustedRow(x.Item.ServiceId, x.Item.Occurrence, x.Item.Reason, x.Pass.StartedAt))];
            return ValueTask.FromResult(rows);
        }
    }

    public ValueTask<RecoveryPassRow?> LatestPassAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var p = Passes.OrderByDescending(x => x.StartedAt).Select(x => (x.Id, x.Trigger, x.StartedAt, x.Status)).FirstOrDefault();
            return ValueTask.FromResult<RecoveryPassRow?>(p.Id == Guid.Empty ? null : new RecoveryPassRow(p.Id, p.Trigger, p.StartedAt, null, p.Status));
        }
    }

    public ValueTask<IReadOnlyList<RecoveryItemRow>> ListSinceAsync(DateOnly since, int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<RecoveryItemRow>>([.. Items.Values.Where(i => i.SessionTo >= since).OrderByDescending(i => i.SessionTo).ThenBy(i => i.ServiceId, StringComparer.Ordinal).Take(limit)]);
        }
    }
}
