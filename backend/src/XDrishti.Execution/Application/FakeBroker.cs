using System.Globalization;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application;

/// <summary>
/// A stateful in-memory broker for tests and the CI rollout step: no network, no state outside the object. It encodes our reading of Dhan's documented behaviour (it can be wrong in the same way as the code): orders are PENDING after an
/// acknowledged place; a limit order fills against a scripted price path (<see cref="SetPrice"/>, in parts when <see cref="MaxFillPerStep"/> is set); a modification is refused after 25; a final order (TRADED, CANCELLED, REJECTED, EXPIRED) is never
/// changed; an expired token answers DH-901; more than 10 writes in one second answer DH-904; <see cref="EndOfDay"/> expires what is open. A repeated client id is acknowledged with the first broker id and stored once.
/// Faults: <see cref="NextOutcome"/> (a scripted NotSent or Unknown that touches nothing), <see cref="DropNextResponse"/> (stored, answer lost), <see cref="RejectNextWith"/> (a coded refusal).
/// </summary>
public sealed class FakeBroker(TimeProvider? clock = null) : IBrokerOrders, IBrokerReads
{
    public const int ModificationCap = 25;
    public const int WritesPerSecondCap = 10;

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, Order> _orders = new(StringComparer.Ordinal);
    private readonly List<BrokerTradeView> _trades = [];
    private readonly Queue<DateTimeOffset> _writes = new();
    private int _next;

    private sealed class Order(BrokerLeg leg, string brokerId)
    {
        public BrokerLeg Leg { get; set; } = leg;

        public string BrokerId { get; } = brokerId;

        public string Status { get; set; } = "PENDING";

        public int Filled { get; set; }

        public int Modifications { get; set; }
    }

    /// <summary>When set, the next call returns it without touching the book (NotSent or Unknown); then it clears.</summary>
    public BrokerOutcome? NextOutcome { get; set; }

    /// <summary>Fault: the next place is stored by the broker but the answer is lost (the caller sees Unknown).</summary>
    public bool DropNextResponse { get; set; }

    /// <summary>Fault: the next write is refused with this code (NotSent), without touching the book; then it clears.</summary>
    public string? RejectNextWith { get; set; }

    /// <summary>The token has expired: every write is refused (DH-901 as <c>token_invalid</c>) and every read is unreadable.</summary>
    public bool TokenExpired { get; set; }

    /// <summary>Largest quantity one price step can fill (0 = no limit); a smaller value makes partial fills.</summary>
    public int MaxFillPerStep { get; set; }

    /// <summary>The funds the funds read reports.</summary>
    public decimal Available { get; set; } = 100000m;

    public int Calls { get; private set; }

    public IReadOnlyCollection<BrokerLeg> Legs
    {
        get
        {
            lock (_gate)
            {
                return [.. _orders.Values.Select(o => o.Leg)];
            }
        }
    }

    public string? StatusOf(string clientOrderId)
    {
        lock (_gate)
        {
            return _orders.TryGetValue(clientOrderId, out var o) ? o.Status : null;
        }
    }

    public int FilledOf(string clientOrderId)
    {
        lock (_gate)
        {
            return _orders.TryGetValue(clientOrderId, out var o) ? o.Filled : 0;
        }
    }

    public Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Calls++;
            if (Refusal() is { } refused)
            {
                return Task.FromResult(refused);
            }

            var order = _orders.TryGetValue(leg.ClientOrderId, out var existing) ? existing : _orders[leg.ClientOrderId] = new Order(leg, "B" + (++_next).ToString(CultureInfo.InvariantCulture));
            if (DropNextResponse)
            {
                DropNextResponse = false;
                return Task.FromResult(new BrokerAck(BrokerOutcome.Unknown, null));
            }

            return Task.FromResult(new BrokerAck(BrokerOutcome.Acknowledged, order.BrokerId));
        }
    }

    public Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Calls++;
            if (Refusal() is { } refused)
            {
                return Task.FromResult(refused);
            }

            if (!_orders.TryGetValue(clientOrderId, out var order) || IsFinal(order.Status))
            {
                return Task.FromResult(new BrokerAck(BrokerOutcome.NotSent, null, "order_not_open"));
            }

            if (order.Modifications >= ModificationCap)
            {
                return Task.FromResult(new BrokerAck(BrokerOutcome.NotSent, null, "modification_cap"));
            }

            order.Modifications++;
            order.Leg = order.Leg with { Price = price };
            return Task.FromResult(new BrokerAck(BrokerOutcome.Acknowledged, order.BrokerId));
        }
    }

    public Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Calls++;
            if (Refusal() is { } refused)
            {
                return Task.FromResult(refused);
            }

            if (!_orders.TryGetValue(clientOrderId, out var order) || IsFinal(order.Status))
            {
                return Task.FromResult(new BrokerAck(BrokerOutcome.NotSent, null, "order_not_open"));
            }

            order.Status = "CANCELLED";
            return Task.FromResult(new BrokerAck(BrokerOutcome.Acknowledged, order.BrokerId));
        }
    }

    public Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_orders.TryGetValue(clientOrderId, out var o) ? o.Leg : null);
        }
    }

    /// <summary>The scripted price path: every open limit order that the price crosses fills (a BUY at or below its limit, a SELL at or above), whole or in parts of <see cref="MaxFillPerStep"/>.</summary>
    public void SetPrice(string securityId, decimal price)
    {
        lock (_gate)
        {
            foreach (var order in _orders.Values.Where(o => !IsFinal(o.Status) && o.Leg.SecurityId == securityId))
            {
                var crosses = order.Leg.Side == ExecSide.Buy ? price <= order.Leg.Price : price >= order.Leg.Price;
                if (!crosses)
                {
                    continue;
                }

                var remaining = order.Leg.Qty - order.Filled;
                var take = MaxFillPerStep > 0 ? Math.Min(remaining, MaxFillPerStep) : remaining;
                order.Filled += take;
                order.Status = order.Filled >= order.Leg.Qty ? "TRADED" : "PART_TRADED";
                _trades.Add(new BrokerTradeView(order.BrokerId, securityId, order.Leg.Side, take, price));
            }
        }
    }

    /// <summary>End of the session: what is still open expires (a part-filled order keeps its fills).</summary>
    public void EndOfDay()
    {
        lock (_gate)
        {
            foreach (var order in _orders.Values.Where(o => !IsFinal(o.Status)))
            {
                order.Status = "EXPIRED";
            }
        }
    }

    public Task<BrokerRead<IReadOnlyList<BrokerOrderView>>> GetOrderBookAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(TokenExpired
                ? BrokerRead.Unreadable<IReadOnlyList<BrokerOrderView>>("token_invalid")
                : BrokerRead.Ok<IReadOnlyList<BrokerOrderView>>([.. _orders.Select(o => new BrokerOrderView(o.Value.BrokerId, o.Key, o.Value.Status, o.Value.Leg.SecurityId, o.Value.Leg.Side, o.Value.Leg.Qty, o.Value.Filled, o.Value.Leg.Price))]));
        }
    }

    public Task<BrokerRead<IReadOnlyList<BrokerTradeView>>> GetTradeBookAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(TokenExpired ? BrokerRead.Unreadable<IReadOnlyList<BrokerTradeView>>("token_invalid") : BrokerRead.Ok<IReadOnlyList<BrokerTradeView>>([.. _trades]));
        }
    }

    public Task<BrokerRead<IReadOnlyList<BrokerPositionView>>> GetPositionsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (TokenExpired)
            {
                return Task.FromResult(BrokerRead.Unreadable<IReadOnlyList<BrokerPositionView>>("token_invalid"));
            }

            var net = _trades.GroupBy(t => t.SecurityId).Select(g => new BrokerPositionView(g.Key, g.Sum(t => t.Side == ExecSide.Buy ? t.Qty : -t.Qty))).ToList();
            return Task.FromResult(BrokerRead.Ok<IReadOnlyList<BrokerPositionView>>(net));
        }
    }

    public Task<BrokerRead<BrokerFundsView>> GetFundsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(TokenExpired ? BrokerRead.Unreadable<BrokerFundsView>("token_invalid") : BrokerRead.Ok<BrokerFundsView>(new BrokerFundsView(Available)));
        }
    }

    private static bool IsFinal(string status) => status is "TRADED" or "CANCELLED" or "REJECTED" or "EXPIRED" or "CLOSED";

    // The scripted faults and the broker-wide refusals, in the order a real broker would meet them. A refused write touches nothing.
    private BrokerAck? Refusal()
    {
        if (NextOutcome is { } scripted)
        {
            NextOutcome = null;
            return new BrokerAck(scripted, null);
        }

        if (TokenExpired)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "token_invalid");
        }

        if (RejectNextWith is { } code)
        {
            RejectNextWith = null;
            return new BrokerAck(BrokerOutcome.NotSent, null, code);
        }

        var now = _clock.GetUtcNow();
        while (_writes.Count > 0 && now - _writes.Peek() >= TimeSpan.FromSeconds(1))
        {
            _writes.Dequeue();
        }

        if (_writes.Count >= WritesPerSecondCap)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "rate_limited_by_broker");
        }

        _writes.Enqueue(now);
        return null;
    }
}
