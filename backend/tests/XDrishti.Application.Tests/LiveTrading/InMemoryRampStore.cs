using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>An in-memory <see cref="IRampStore"/> that stamps each event with the clock, as the table does, and keeps hold and exit confirmation in the state.</summary>
internal sealed class InMemoryRampStore(TimeProvider clock) : IRampStore
{
    public List<StoredRampEvent> Rows { get; } = [];

    public void Seed(Guid accountId, string kind, int? from, int to, bool hold, DateTimeOffset at) =>
        Rows.Add(new StoredRampEvent(Rows.Count + 1, accountId, kind, from, to, hold, kind is RampEventKinds.SetStep or RampEventKinds.ClearHold or RampEventKinds.ExitConfirmed ? "owner:hem" : RampEventKinds.RuleActor, "seed", null, at));

    public ValueTask<RampState?> CurrentAsync(Guid accountId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Rows.LastOrDefault(r => r.AccountId == accountId) is { } r ? new RampState(r.Id, r.ToStep, r.Hold, Rows.Any(x => x.AccountId == accountId && x.Kind == RampEventKinds.ExitConfirmed)) : null);

    public async ValueTask<IReadOnlyDictionary<Guid, RampState>> CurrentAllAsync(CancellationToken cancellationToken)
    {
        var all = new Dictionary<Guid, RampState>();
        foreach (var id in Rows.Select(r => r.AccountId).Distinct())
        {
            all[id] = (await CurrentAsync(id, cancellationToken))!;
        }

        return all;
    }

    public ValueTask<IReadOnlyList<StoredRampEvent>> HistoryAsync(Guid accountId, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredRampEvent>>([.. Rows.Where(r => r.AccountId == accountId).OrderByDescending(r => r.Id).Take(limit)]);

    public ValueTask<StoredRampEvent?> AppendAsync(RampEvent ramp, CancellationToken cancellationToken)
    {
        var row = new StoredRampEvent(Rows.Count + 1, ramp.AccountId, ramp.Kind, ramp.FromStep, ramp.ToStep, ramp.Hold, ramp.Actor, ramp.Reason, ramp.Gates?.Hash, clock.GetUtcNow(), ramp.Gates?.Text);
        Rows.Add(row);
        return ValueTask.FromResult<StoredRampEvent?>(row);
    }
}
