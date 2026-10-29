using System.Collections.Concurrent;
using System.Globalization;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application;

/// <summary>An in-memory broker for tests and the CI rollout step: no network, no state outside the object. A repeated client id is acknowledged with the first broker id and stored once. <see cref="NextOutcome"/> scripts a failure.</summary>
public sealed class FakeBroker : IBrokerOrders
{
    private readonly ConcurrentDictionary<string, (BrokerLeg Leg, string BrokerId)> _legs = new(StringComparer.Ordinal);
    private int _next;

    /// <summary>When set, the next call returns it without touching the book (NotSent or Unknown); then it clears.</summary>
    public BrokerOutcome? NextOutcome { get; set; }

    /// <summary>Fault: the next place is stored by the broker but the answer is lost (the caller sees Unknown).</summary>
    public bool DropNextResponse { get; set; }

    public int Calls { get; private set; }

    public IReadOnlyCollection<BrokerLeg> Legs => [.. _legs.Values.Select(v => v.Leg)];

    public Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken)
    {
        Calls++;
        if (Scripted() is { } scripted)
        {
            return Task.FromResult(new BrokerAck(scripted, null));
        }

        var stored = _legs.GetOrAdd(leg.ClientOrderId, _ => (leg, "B" + Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture)));
        if (DropNextResponse)
        {
            DropNextResponse = false;
            return Task.FromResult(new BrokerAck(BrokerOutcome.Unknown, null));
        }

        return Task.FromResult(new BrokerAck(BrokerOutcome.Acknowledged, stored.BrokerId));
    }

    public Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken)
    {
        Calls++;
        if (Scripted() is { } scripted)
        {
            return Task.FromResult(new BrokerAck(scripted, null));
        }

        if (!_legs.TryGetValue(clientOrderId, out var found))
        {
            return Task.FromResult(new BrokerAck(BrokerOutcome.NotSent, null));
        }

        _legs[clientOrderId] = (found.Leg with { Price = price }, found.BrokerId);
        return Task.FromResult(new BrokerAck(BrokerOutcome.Acknowledged, found.BrokerId));
    }

    public Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        Calls++;
        if (Scripted() is { } scripted)
        {
            return Task.FromResult(new BrokerAck(scripted, null));
        }

        return Task.FromResult(_legs.TryRemove(clientOrderId, out var found)
            ? new BrokerAck(BrokerOutcome.Acknowledged, found.BrokerId)
            : new BrokerAck(BrokerOutcome.NotSent, null));
    }

    public Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(_legs.TryGetValue(clientOrderId, out var found) ? found.Leg : null);

    private BrokerOutcome? Scripted()
    {
        var outcome = NextOutcome;
        NextOutcome = null;
        return outcome;
    }
}
