using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>An in-memory <see cref="ILiveHaltStore"/> with the real unique key (account, kind, period, event).</summary>
internal sealed class InMemoryHaltStore : ILiveHaltStore
{
    public List<StoredHalt> Rows { get; } = [];

    public ValueTask<StoredHalt?> OpenAsync(NewHalt halt, CancellationToken cancellationToken)
    {
        if (Rows.Any(r => r.AccountId == halt.AccountId && r.Kind == halt.Kind && r.PeriodStart == halt.PeriodStart && r.Event == "opened"))
        {
            return ValueTask.FromResult<StoredHalt?>(null);
        }

        var row = new StoredHalt(Rows.Count + 1, halt.AccountId, halt.Kind, "opened", halt.PeriodStart, halt.LimitR, halt.LimitPct, halt.MeasuredR, halt.MeasuredPct, halt.MeasureState, halt.Action, DateTimeOffset.UnixEpoch.AddDays(halt.PeriodStart.DayNumber));
        Rows.Add(row);
        return ValueTask.FromResult<StoredHalt?>(row);
    }

    public ValueTask<IReadOnlyList<StoredHalt>> ListAsync(Guid? accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredHalt>>([.. Rows.Where(r => (accountId is null || r.AccountId == accountId) && r.PeriodStart >= from && r.PeriodStart <= to).OrderByDescending(r => r.Id)]);
}
