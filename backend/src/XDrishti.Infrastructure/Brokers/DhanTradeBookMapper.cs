using System.Text.Json;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Turns Dhan's trade book of the day (<c>GET /v2/trades</c>, spec P11-01, ADR 0149) into fills, field names as in <see cref="DhanTradeMapper"/> (any case, numbers as text, <c>"NA"</c> times missing). Unlike the history mapper every product of
/// <c>NSE_EQ</c> is kept (the product is stored): other segments are skipped and counted per <see cref="BrokerSkipReasons"/>, and a row without an id, side, whole positive quantity, positive price or readable time is dropped and counted by
/// <see cref="BrokerDropReasons"/>, never stored half-read. Pure; nothing of a row's content is kept in a count.
/// </summary>
internal static class DhanTradeBookMapper
{
    private const decimal Limit = 1_000_000_000_000m;
    private static readonly string[] TimeFields = ["exchangeTime", "createTime", "updateTime"];

    public static LiveBookRead<LiveBrokerFill> Map(JsonElement rows)
    {
        var fills = new List<LiveBrokerFill>();
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var drops = new List<BrokerRowDrop>();
        var count = 0;
        if (rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var (fill, skip, drop) = Read(row);
                if (fill is not null)
                {
                    fills.Add(fill);
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

        return new LiveBookRead<LiveBrokerFill>(fills, count, drops.Count, skipped, BrokerReadDrops.From(drops));
    }

    private static (LiveBrokerFill? Fill, string? Skip, string? Drop) Read(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return (null, null, BrokerDropReasons.UnreadableRow);
        }

        if (DhanTradeMapper.SkipReason(DhanJson.Text(row, "exchangeSegment", "segment"), null) is { } skip)
        {
            return (null, skip, null);
        }

        var id = DhanJson.Text(row, "exchangeTradeId", "tradeId");
        if (id is null)
        {
            return (null, null, BrokerDropReasons.MissingId);
        }

        if (id.Length > DhanTradeMapper.MaxTradeIdLength)
        {
            return (null, null, BrokerDropReasons.BadId);
        }

        if (!TradeRules.TryParseSide(DhanJson.Text(row, "transactionType", "side"), out var side))
        {
            return (null, null, BrokerDropReasons.MissingSide);
        }

        if (DhanJson.Number(row, Limit, "tradedQuantity", "quantity") is not { } qty || qty <= 0m || qty != decimal.Truncate(qty))
        {
            return (null, null, BrokerDropReasons.BadQuantity);
        }

        if (DhanJson.Number(row, Limit, "tradedPrice", "price") is not { } price || price <= 0m)
        {
            return (null, null, BrokerDropReasons.BadPrice);
        }

        if (Moment(row) is not { } at)
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

        var product = (DhanJson.Known(row, "productType", "product") ?? "UNKNOWN").ToUpperInvariant();
        return (new LiveBrokerFill(id, DhanJson.Known(row, "orderId", "exchangeOrderId"), side == TradeSide.Buy ? Domain.Research.Side.Buy : Domain.Research.Side.Sell, symbol, isin, securityId, DhanBookText.Cap(product, 12) ?? "UNKNOWN", (long)qty, price, at.Date, at.Time), null, null);
    }

    private static (DateOnly Date, TimeOnly? Time)? Moment(JsonElement row)
    {
        foreach (var name in TimeFields)
        {
            if (DhanJson.DateTime(DhanJson.Known(row, name)) is { } moment)
            {
                return moment;
            }
        }

        return null;
    }
}
