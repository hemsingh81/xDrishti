using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Application.Recon;

public enum ApplyOutcome
{
    /// <summary>The leg moved.</summary>
    Applied,

    /// <summary>The same update was seen before.</summary>
    Duplicate,

    /// <summary>A lower status than the leg's (or any change after a final one): counted, ignored.</summary>
    Stale,

    /// <summary>Our prefix, no leg: a break for the reconciliation, never a guess.</summary>
    UnknownXd,

    /// <summary>Not our order.</summary>
    Foreign,
}

/// <summary>A leg the stream and the poll keep current: the domain leg (rank rule, never-decreasing fill) plus the broker's order id and the average price of the provisional fills.</summary>
public sealed class TrackedLeg(string clientOrderId, long intentId, Leg leg)
{
    public string ClientOrderId { get; } = clientOrderId;

    public long IntentId { get; } = intentId;

    public Leg Leg { get; } = leg;

    public string? BrokerOrderId { get; set; }

    public decimal AvgPrice { get; set; }

    public DateTimeOffset LastChangeAt { get; set; }
}

/// <summary>The legs by client order id and broker order id.</summary>
public sealed class LegBook
{
    private readonly Dictionary<string, TrackedLeg> _byClient = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedLeg> _byBroker = new(StringComparer.Ordinal);

    public IReadOnlyCollection<TrackedLeg> All => _byClient.Values;

    public TrackedLeg Add(string clientOrderId, long intentId, LegKind kind, int qty, string? brokerOrderId = null)
    {
        var tracked = new TrackedLeg(clientOrderId, intentId, new Leg(kind, qty)) { BrokerOrderId = brokerOrderId };
        _byClient[clientOrderId] = tracked;
        Index(tracked);
        return tracked;
    }

    public TrackedLeg? ByClient(string? clientOrderId) => clientOrderId is not null && _byClient.TryGetValue(clientOrderId, out var t) ? t : null;

    public TrackedLeg? ByBroker(string orderNo) => _byBroker.GetValueOrDefault(orderNo);

    public void Index(TrackedLeg tracked)
    {
        if (tracked.BrokerOrderId is { } id)
        {
            _byBroker[id] = tracked;
        }
    }
}

/// <summary>Counts of what the stream did, shown by the stream status.</summary>
public sealed class UpdateCounters
{
    private long _applied;
    private long _ignored;
    private long _dropped;

    public long Applied => Interlocked.Read(ref _applied);

    public long Ignored => Interlocked.Read(ref _ignored);

    public long Dropped => Interlocked.Read(ref _dropped);

    internal void Apply() => Interlocked.Increment(ref _applied);

    internal void Ignore() => Interlocked.Increment(ref _ignored);

    public void Drop() => Interlocked.Increment(ref _dropped);
}

/// <summary>
/// Applies order updates to the legs (spec P12-06): once per key (order, status, traded quantity, time), a lower rank than the leg's ignored and counted (the P12-01 rank rule is <see cref="Leg.Apply"/>), the filled quantity never decreasing. A leg is found by correlation id
/// (ours) or by its broker order id (the stop and target legs the broker creates under our super order). An update for our prefix with no leg is reported, not invented. The average price of provisional fills moves only when the filled quantity grows.
/// </summary>
public sealed class UpdateApplier(LegBook legs, UpdateCounters counters, TimeProvider clock)
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _unknownXd = [];

    public IReadOnlyList<string> UnknownXd => _unknownXd;

    /// <summary>Applies one order of the broker's order book (the poll). The same rank rule as the stream; an unknown status text moves nothing.</summary>
    public ApplyOutcome ApplyPoll(Ports.BrokerOrderView order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var leg = legs.ByClient(order.ClientOrderId) ?? legs.ByBroker(order.OrderId);
        if (leg is null)
        {
            counters.Ignore();
            return order.ClientOrderId?.StartsWith(Reconciler.Prefix, StringComparison.Ordinal) ?? false ? ApplyOutcome.UnknownXd : ApplyOutcome.Foreign;
        }

        if (ToLegStatus(order.Status, order.FilledQty, order.Qty) is not { } status)
        {
            counters.Ignore();
            return ApplyOutcome.Stale;
        }

        var before = leg.Leg.FilledQty;
        if (!leg.Leg.Apply(status, order.FilledQty))
        {
            counters.Ignore();
            return ApplyOutcome.Stale;
        }

        leg.BrokerOrderId ??= order.OrderId;
        legs.Index(leg);
        if (leg.Leg.FilledQty != before || leg.Leg.Status != status)
        {
            leg.LastChangeAt = clock.GetUtcNow();
        }

        counters.Apply();
        return ApplyOutcome.Applied;
    }

    public static LegStatus? ToLegStatus(string status, int filled, int qty) => status.ToUpperInvariant() switch
    {
        "TRADED" => LegStatus.Traded,
        "CANCELLED" => LegStatus.Cancelled,
        "REJECTED" => LegStatus.Rejected,
        "EXPIRED" => LegStatus.Expired,
        "CLOSED" => LegStatus.Closed,
        "PART_TRADED" => LegStatus.PartTraded,
        "TRANSIT" => LegStatus.Transit,
        "PENDING" or "CONFIRMED" or "TRIGGERED" => filled > 0 && filled < qty ? LegStatus.PartTraded : LegStatus.Pending,
        _ => null,
    };

    public ApplyOutcome Apply(OrderUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var leg = legs.ByClient(update.CorrelationId) ?? legs.ByBroker(update.OrderNo);
        if (leg is null)
        {
            if (update.CorrelationId?.StartsWith(Reconciler.Prefix, StringComparison.Ordinal) ?? false)
            {
                _unknownXd.Add(update.OrderNo);
                counters.Ignore();
                return ApplyOutcome.UnknownXd;
            }

            counters.Ignore();
            return ApplyOutcome.Foreign;
        }

        if (!_seen.Add(update.Key))
        {
            counters.Ignore();
            return ApplyOutcome.Duplicate;
        }

        var before = leg.Leg.FilledQty;
        if (!leg.Leg.Apply(update.LegStatus, update.TradedQty))
        {
            counters.Ignore();
            return ApplyOutcome.Stale;
        }

        leg.BrokerOrderId ??= update.OrderNo;
        legs.Index(leg);
        if (leg.Leg.FilledQty > before && update.AvgTradedPrice > 0m)
        {
            leg.AvgPrice = update.AvgTradedPrice;
        }

        leg.LastChangeAt = clock.GetUtcNow();
        counters.Apply();
        return ApplyOutcome.Applied;
    }
}
