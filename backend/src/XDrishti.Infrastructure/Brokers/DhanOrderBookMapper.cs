using System.Text.Json;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Turns Dhan's order book of the day (<c>GET /v2/orders</c>, spec P11-01, ADR 0149) into order revisions: <c>orderId</c>, <c>orderStatus</c> (one of the seven states, any case; another word - a super-order leg state such as TRIGGERED or CLOSED - is a dropped row
/// counted as <see cref="BrokerDropReasons.Other"/>), <c>transactionType</c>, <c>productType</c>, <c>orderType</c>, <c>validity</c>, <c>quantity</c>, <c>price</c> and <c>triggerPrice</c> (0 or missing is none), <c>filledQty</c> (else quantity less <c>remainingQuantity</c>, else the whole
/// quantity of a traded order, else 0), <c>averageTradedPrice</c>, the times (<c>createTime</c> is required; India time), <c>legName</c>, <c>algoId</c> and <c>omsErrorDescription</c> (length-capped and stripped of control characters: it is data, never markup).
/// Other segments are skipped per reason; a row without an id, side, status, whole positive quantity or a readable create time is dropped by reason. Pure; no count carries a row's content.
/// </summary>
internal static class DhanOrderBookMapper
{
    public const int MaxOrderIdLength = 40;
    public const int MaxRejectText = 200;
    private const decimal Limit = 1_000_000_000_000m;

    public static LiveBookRead<LiveBrokerOrder> Map(JsonElement rows)
    {
        var orders = new List<LiveBrokerOrder>();
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var drops = new List<BrokerRowDrop>();
        var count = 0;
        if (rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var (order, skip, drop) = Read(row);
                if (order is not null)
                {
                    orders.Add(order);
                }
                else if (skip is not null)
                {
                    skipped[skip] = skipped.GetValueOrDefault(skip) + 1;
                }
                else
                {
                    drops.Add(new BrokerRowDrop(count, drop ?? BrokerDropReasons.Other));
                }

                count++;
            }
        }

        return new LiveBookRead<LiveBrokerOrder>(orders, count, drops.Count, skipped, BrokerReadDrops.From(drops));
    }

    private static (LiveBrokerOrder? Order, string? Skip, string? Drop) Read(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return (null, null, BrokerDropReasons.UnreadableRow);
        }

        if (DhanTradeMapper.SkipReason(DhanJson.Text(row, "exchangeSegment", "segment"), null) is { } skip)
        {
            return (null, skip, null);
        }

        var id = DhanJson.Text(row, "orderId");
        if (id is null)
        {
            return (null, null, BrokerDropReasons.MissingId);
        }

        if (id.Length > MaxOrderIdLength)
        {
            return (null, null, BrokerDropReasons.BadId);
        }

        if (!TradeRules.TryParseSide(DhanJson.Text(row, "transactionType", "side"), out var side))
        {
            return (null, null, BrokerDropReasons.MissingSide);
        }

        if (DhanJson.Number(row, Limit, "quantity") is not { } qty || qty <= 0m || qty != decimal.Truncate(qty))
        {
            return (null, null, BrokerDropReasons.BadQuantity);
        }

        if (LiveOrderStatus.Parse(DhanJson.Text(row, "orderStatus", "status")) is not { } status)
        {
            return (null, null, BrokerDropReasons.Other);
        }

        if (Moment(row, "createTime") is not { } created)
        {
            return (null, null, BrokerDropReasons.BadTime);
        }

        var symbol = DhanJson.Known(row, "tradingSymbol");
        var isin = DhanBookIdentity.Isin(DhanJson.Known(row, "isin"));
        var securityId = DhanJson.Known(row, "securityId");
        if (symbol is null && isin is null && securityId is null)
        {
            return (null, BrokerSkipReasons.NoSymbol, null);
        }

        var filled = Filled(row, (long)qty, status);
        return (
            new LiveBrokerOrder(
                id,
                symbol,
                isin,
                securityId,
                side == TradeSide.Buy ? Domain.Research.Side.Buy : Domain.Research.Side.Sell,
                DhanBookText.Cap((DhanJson.Known(row, "productType", "product") ?? "UNKNOWN").ToUpperInvariant(), 12) ?? "UNKNOWN",
                DhanBookText.Cap((DhanJson.Known(row, "orderType") ?? "UNKNOWN").ToUpperInvariant(), 20) ?? "UNKNOWN",
                DhanBookText.Cap((DhanJson.Known(row, "validity") ?? "DAY").ToUpperInvariant(), 8) ?? "DAY",
                (long)qty,
                Positive(row, "price"),
                Positive(row, "triggerPrice"),
                status,
                filled,
                Positive(row, "averageTradedPrice"),
                created,
                Moment(row, "exchangeTime"),
                Moment(row, "updateTime"),
                DhanBookText.Cap(DhanJson.Known(row, "legName"), 24),
                DhanBookText.Cap(DhanJson.Known(row, "algoId"), 40),
                DhanBookText.Cap(DhanJson.Known(row, "omsErrorDescription"), MaxRejectText)),
            null,
            null);
    }

    /// <summary>A price of an order: missing, "NA", zero or negative is none (a market order has price 0).</summary>
    private static decimal? Positive(JsonElement row, string name) =>
        DhanJson.Number(row, Limit, name) is > 0m and var value ? value : null;

    private static long Filled(JsonElement row, long qty, string status)
    {
        if (DhanJson.Number(row, Limit, "filledQty", "tradedQuantity") is { } filled && filled >= 0m && filled == decimal.Truncate(filled))
        {
            return Math.Min((long)filled, qty);
        }

        if (DhanJson.Number(row, Limit, "remainingQuantity") is { } remaining && remaining >= 0m && remaining <= qty && remaining == decimal.Truncate(remaining))
        {
            return qty - (long)remaining;
        }

        return status == LiveOrderStatus.Traded ? qty : 0;
    }

    private static DateTimeOffset? Moment(JsonElement row, string name) =>
        DhanJson.DateTime(DhanJson.Known(row, name)) is { Time: { } time } moment ? new DateTimeOffset(moment.Date.ToDateTime(time), IndiaTime.Offset) : null;
}
