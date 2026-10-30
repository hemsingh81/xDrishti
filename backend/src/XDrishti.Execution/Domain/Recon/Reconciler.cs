using System.Globalization;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Domain.Recon;

/// <summary>
/// The comparison of our record with the broker's (spec P12-06), pure: it reads its input and returns breaks, it corrects nothing and has no way to. Order of work: orders against legs (status class, quantities, with a grace window for what is still in flight),
/// the broker's orders with our prefix against legs (a stop or target leg of our super order is matched by parent, leg number and recorded order id before anything is called unknown), trades against fills by exchange trade id, then positions in the
/// instruments we manage against our book plus the explained foreign trades. A foreign working order in a managed instrument is a conflict. Anything the broker view cannot say (a trade without an exchange id) is <c>unavailable</c>, never agreement.
/// </summary>
public static class Reconciler
{
    public const string Prefix = "XD";

    private enum Class
    {
        InFlight,
        Working,
        Traded,
        Ended,
        Unknown,
    }

    public static ReconResult Compare(ReconInput i)
    {
        ArgumentNullException.ThrowIfNull(i);
        var found = new List<ReconBreakDraft>();
        var byCid = i.Legs.GroupBy(l => l.ClientOrderId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byBrokerId = i.Legs.Where(l => l.BrokerOrderId is not null).GroupBy(l => l.BrokerOrderId!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var managed = i.Legs.Select(l => l.SecurityId).ToHashSet(StringComparer.Ordinal);
        var matchedLegs = new HashSet<string>(StringComparer.Ordinal);
        var ourOrderIds = new HashSet<string>(StringComparer.Ordinal);
        var foreignOrders = new Dictionary<string, int>(StringComparer.Ordinal);
        var foreignNet = new Dictionary<string, int>(StringComparer.Ordinal);
        var foreignOrderIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var order in i.BrokerOrders)
        {
            var (leg, wrongId) = Match(order, byCid, byBrokerId, i.Legs);
            if (leg is not null && !wrongId)
            {
                matchedLegs.Add(leg.ClientOrderId);
                ourOrderIds.Add(order.OrderId);
                CompareOrder(i, order, leg, found);
            }
            else if (leg is not null)
            {
                matchedLegs.Add(leg.ClientOrderId);
                ourOrderIds.Add(order.OrderId);
                found.Add(new ReconBreakDraft(BreakKind.OrderMismatch, $"order_mismatch:{leg.ClientOrderId}:other_order:{order.OrderId}", order.SecurityId, leg.IntentId, $"{leg.BrokerOrderId}", order.OrderId, "a leg under our parent has another order id than recorded"));
            }
            else if ((order.ClientOrderId?.StartsWith(Prefix, StringComparison.Ordinal) ?? false) || (order.ParentOrderId is not null && byBrokerId.ContainsKey(order.ParentOrderId)))
            {
                ourOrderIds.Add(order.OrderId);
                found.Add(new ReconBreakDraft(BreakKind.UnknownXdOrder, $"unknown_xd_order:{order.OrderId}", order.SecurityId, null, "no leg", $"{order.OrderId} {order.Status}", "an order with our prefix or under our super order has no leg in our record"));
            }
            else
            {
                foreignOrderIds.Add(order.OrderId);
                foreignOrders[order.SecurityId] = foreignOrders.GetValueOrDefault(order.SecurityId) + 1;
                if (ClassOf(order.Status) == Class.Working && managed.Contains(order.SecurityId))
                {
                    found.Add(new ReconBreakDraft(BreakKind.ForeignConflict, $"foreign_conflict:{order.OrderId}", order.SecurityId, null, "managed instrument", $"{order.OrderId} {order.Status}", "a working order we did not place in an instrument we manage"));
                }
            }
        }

        foreach (var leg in i.Legs.Where(l => !matchedLegs.Contains(l.ClientOrderId)))
        {
            if (leg.Status is LegStatus.New or LegStatus.Rejected || i.Now - leg.LastChangeAt <= i.Grace)
            {
                continue;
            }

            found.Add(new ReconBreakDraft(BreakKind.OrderMismatch, $"order_mismatch:{leg.ClientOrderId}:absent", leg.SecurityId, leg.IntentId, leg.Status.ToString(), "absent", "a leg we recorded as sent is not in the broker's order book"));
        }

        var missingNow = new HashSet<string>(StringComparer.Ordinal);
        var ourFillsById = i.Fills.GroupBy(f => f.ExchangeTradeId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var seenTradeIds = new HashSet<string>(StringComparer.Ordinal);
        var tradeIdsMissing = false;
        foreach (var trade in i.BrokerTrades)
        {
            var ours = ourOrderIds.Contains(trade.OrderId) && !foreignOrderIds.Contains(trade.OrderId);
            if (!ours)
            {
                var signed = trade.Side == ExecSide.Sell ? -trade.Qty : trade.Qty;
                foreignNet[trade.SecurityId] = foreignNet.GetValueOrDefault(trade.SecurityId) + signed;
                continue;
            }

            if (trade.ExchangeTradeId is not { Length: > 0 } id)
            {
                tradeIdsMissing = true;
                continue;
            }

            seenTradeIds.Add(id);
            if (!ourFillsById.TryGetValue(id, out var fill))
            {
                if (i.PreviousMissingTrades.Contains(id))
                {
                    found.Add(new ReconBreakDraft(BreakKind.TradeMissing, $"trade_missing:{id}", trade.SecurityId, null, "no fill", $"{trade.Qty} @ {Fmt(trade.Price)}", "a broker trade of our order is not in our fills after two polls"));
                }
                else
                {
                    missingNow.Add(id);
                }

                continue;
            }

            if (fill.Qty != trade.Qty || Math.Round(fill.Price, 4) != Math.Round(trade.Price, 4))
            {
                found.Add(new ReconBreakDraft(BreakKind.FillMismatch, $"fill_mismatch:{id}:{trade.Qty}:{Fmt(trade.Price)}", trade.SecurityId, null, $"{fill.Qty} @ {Fmt(fill.Price)}", $"{trade.Qty} @ {Fmt(trade.Price)}", "a fill differs from the broker's trade"));
            }
        }

        if (tradeIdsMissing)
        {
            found.Add(new ReconBreakDraft(BreakKind.Unavailable, "unavailable:trade_ids_missing", null, null, "n/a", "n/a", "the broker's trade book has no exchange trade id for a trade of our order"));
        }

        foreach (var fill in i.Fills.Where(f => !seenTradeIds.Contains(f.ExchangeTradeId)))
        {
            found.Add(new ReconBreakDraft(BreakKind.TradeExtra, $"trade_extra:{fill.ExchangeTradeId}", fill.SecurityId, null, $"{fill.Qty} @ {Fmt(fill.Price)}", "absent", "a fill we hold is not in the broker's trade book"));
        }

        var fillSums = i.Fills.GroupBy(f => f.ClientOrderId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(f => f.Qty), StringComparer.Ordinal);
        foreach (var leg in i.Legs.Where(l => l.Status is not (LegStatus.New or LegStatus.Sent) && i.Now - l.LastChangeAt > i.Grace))
        {
            var sum = fillSums.GetValueOrDefault(leg.ClientOrderId);
            if (sum != leg.FilledQty)
            {
                found.Add(new ReconBreakDraft(BreakKind.FillMismatch, $"fill_mismatch:{leg.ClientOrderId}:{sum}:{leg.FilledQty}", leg.SecurityId, leg.IntentId, $"leg {leg.FilledQty}", $"fills {sum}", "the leg's cumulative quantity and the sum of its fills disagree"));
            }
        }

        var ourNet = i.Positions.ToDictionary(p => p.SecurityId, p => p.NetQty, StringComparer.Ordinal);
        var theirNet = i.BrokerPositions.GroupBy(p => p.SecurityId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(p => p.NetQty), StringComparer.Ordinal);
        foreach (var security in managed.Order(StringComparer.Ordinal))
        {
            var expected = ourNet.GetValueOrDefault(security) + foreignNet.GetValueOrDefault(security);
            var actual = theirNet.GetValueOrDefault(security);
            if (expected != actual && !i.Legs.Any(l => l.SecurityId == security && i.Now - l.LastChangeAt <= i.Grace))
            {
                found.Add(new ReconBreakDraft(BreakKind.PositionMismatch, $"position_mismatch:{security}:{actual}", security, null, $"{expected} (ours {ourNet.GetValueOrDefault(security)}, foreign {foreignNet.GetValueOrDefault(security)})", actual.ToString(CultureInfo.InvariantCulture), "the broker's net position differs from ours after the explained foreign trades"));
            }
        }

        var accepted = 0;
        List<ReconBreakDraft> breaks = [];
        foreach (var b in found.DistinctBy(b => b.Key, StringComparer.Ordinal).OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            if (i.Accepted.Contains(b.Key))
            {
                accepted++;
            }
            else
            {
                breaks.Add(b);
            }
        }

        var foreign = foreignOrders.Keys.Union(foreignNet.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(s => new ForeignItem(s, foreignNet.GetValueOrDefault(s), foreignOrders.GetValueOrDefault(s))).ToList();
        return new ReconResult(breaks, i.BrokerOrders.Count, i.BrokerTrades.Count, i.BrokerPositions.Count, accepted, foreign, missingNow);
    }

    private static void CompareOrder(ReconInput i, BrokerOrderView order, OurLegView leg, List<ReconBreakDraft> found)
    {
        if (i.Now - leg.LastChangeAt <= i.Grace)
        {
            return;
        }

        var theirs = ClassOf(order.Status);
        var ours = ClassOf(leg);
        string? why = null;
        if (theirs == Class.Unknown)
        {
            why = "status_unknown";
        }
        else if (ours != theirs && !(ours == Class.InFlight && theirs == Class.Working))
        {
            why = "status";
        }
        else if (ours == Class.InFlight)
        {
            why = "status";
        }
        else if (order.Qty != leg.Qty || order.FilledQty != leg.FilledQty)
        {
            why = "quantity";
        }

        if (why is not null)
        {
            found.Add(new ReconBreakDraft(BreakKind.OrderMismatch, $"order_mismatch:{leg.ClientOrderId}:{order.Status}:{order.FilledQty}:{order.Qty}", leg.SecurityId, leg.IntentId, $"{leg.Status} {leg.FilledQty}/{leg.Qty}", $"{order.Status} {order.FilledQty}/{order.Qty}", $"the order's {why} differs beyond the grace"));
        }
    }

    private static (OurLegView? Leg, bool WrongId) Match(BrokerOrderView order, Dictionary<string, OurLegView> byCid, Dictionary<string, OurLegView> byBrokerId, IReadOnlyList<OurLegView> legs)
    {
        if (order.ClientOrderId is { } cid && byCid.TryGetValue(cid, out var byClient))
        {
            return (byClient, false);
        }

        if (byBrokerId.TryGetValue(order.OrderId, out var byId))
        {
            return (byId, false);
        }

        if (order.ParentOrderId is { } parent && LegNo(order.LegName) is { } no
            && legs.FirstOrDefault(l => l.ParentBrokerOrderId == parent && l.LegNo == no) is { } child)
        {
            return (child, child.BrokerOrderId != order.OrderId);
        }

        return (null, false);
    }

    /// <summary>The leg number of a super-order leg name (UNVERIFIED names): entry 1, stop 2, target 3.</summary>
    public static int? LegNo(string? name) => name?.ToUpperInvariant() switch
    {
        "ENTRY_LEG" => 1,
        "STOP_LOSS_LEG" => 2,
        "TARGET_LEG" => 3,
        _ => null,
    };

    private static Class ClassOf(OurLegView leg) => leg.Status switch
    {
        LegStatus.New or LegStatus.Sent => Class.InFlight,
        LegStatus.Transit or LegStatus.Pending or LegStatus.PartTraded => Class.Working,
        LegStatus.Traded => Class.Traded,
        _ => Class.Ended,
    };

    private static Class ClassOf(string status) => status.ToUpperInvariant() switch
    {
        "TRADED" => Class.Traded,
        "CANCELLED" or "REJECTED" or "EXPIRED" or "CLOSED" => Class.Ended,
        "TRANSIT" or "PENDING" or "PART_TRADED" or "CONFIRMED" or "TRIGGERED" => Class.Working,
        _ => Class.Unknown,
    };

    private static string Fmt(decimal v) => Math.Round(v, 4).ToString("0.0000", CultureInfo.InvariantCulture);
}
