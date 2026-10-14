using System.Text.Json;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Turns one page of Dhan's trade history (ADR 0025, <c>GET /v2/trades/{from}/{to}/{page}</c>) into NSE equity trades. A row is read by <c>exchangeTradeId</c> (else <c>tradeId</c>), <c>orderId</c>, <c>transactionType</c> (buy or sell),
/// <c>exchangeSegment</c>, <c>productType</c>, <c>tradingSymbol</c>, <c>securityId</c>, <c>tradedQuantity</c>, <c>tradedPrice</c> and the time <c>exchangeTime</c> (else <c>createTime</c>, else <c>updateTime</c>), India time; names match in any case and
/// numbers may be text (<see cref="DhanJson"/>). Only <c>NSE_EQ</c> rows that are not intraday products are kept (delivery, <c>MTF</c>, <c>MARGIN</c>): other segments are skipped and counted per reason (<see cref="BrokerSkipReasons"/>), so are intraday, cover and bracket orders, which net out within the day and
/// never reach the holdings. A row without an id, a ticker, a side, a whole positive quantity, a positive price or a readable date is dropped and counted. Dhan's field names are from its public documentation, not from a real answer of the owner's account. Pure.
/// </summary>
internal static class DhanTradeMapper
{
    /// <summary>The longest trade id kept (the identity is the date and the id).</summary>
    public const int MaxTradeIdLength = 64;

    private const decimal Limit = 1_000_000_000_000m;

    /// <summary>
    /// Products whose trades net out within the day and never reach the holdings. <c>MTF</c> (margin trading funding) and <c>CNC</c> are delivery and stay. <c>MARGIN</c> is not listed: Dhan documents it as the carry-forward product of futures and
    /// options, which are skipped by their segment, so on <c>NSE_EQ</c> it can only be a position carried overnight - a trade the holdings may contain - and skipping it would leave shares out of the reconstruction (a quantity mismatch that
    /// stops the rebuild), while storing an intraday pair that nets to zero costs nothing.
    /// </summary>
    private static readonly string[] IntradayProducts = ["INTRADAY", "INTRA", "MIS", "CO", "BO"];

    public static BrokerTradePage Map(JsonElement rows)
    {
        var trades = new List<BrokerTrade>(rows.ValueKind == JsonValueKind.Array ? rows.GetArrayLength() : 0);
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var count = 0;
        var dropped = 0;
        if (rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                count++;
                switch (Read(row))
                {
                    case { Trade: { } trade }:
                        trades.Add(trade);
                        break;
                    case { Skip: { } reason }:
                        skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                        break;
                    default:
                        dropped++;
                        break;
                }
            }
        }

        return new BrokerTradePage(trades, count, dropped, skipped);
    }

    private readonly record struct RowResult(BrokerTrade? Trade, string? Skip);

    private static RowResult Read(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        if (SkipReason(DhanJson.Text(row, "exchangeSegment", "segment"), DhanJson.Text(row, "productType", "product")) is { } skip)
        {
            return new RowResult(null, skip);
        }

        var id = DhanJson.Text(row, "exchangeTradeId", "tradeId");
        var symbol = DhanJson.Text(row, "tradingSymbol");
        var qty = DhanJson.Number(row, Limit, "tradedQuantity", "quantity");
        var price = DhanJson.Number(row, Limit, "tradedPrice", "price");
        var moment = DhanJson.DateTime(DhanJson.Text(row, "exchangeTime", "createTime", "updateTime"));
        if (id is null || id.Length > MaxTradeIdLength || symbol is null || !TradeRules.TryParseSide(DhanJson.Text(row, "transactionType", "side"), out var side)
            || qty is not { } quantity || quantity <= 0m || quantity != decimal.Truncate(quantity) || price is not { } traded || traded <= 0m || moment is not { } when)
        {
            return default;
        }

        return new RowResult(
            new BrokerTrade(id, DhanJson.Text(row, "orderId", "exchangeOrderId"), side, symbol, DhanJson.Text(row, "securityId"), (long)quantity, traded, when.Date, when.Time),
            null);
    }

    /// <summary>Why the row is not a delivery trade of the NSE cash segment; null when it is one.</summary>
    internal static string? SkipReason(string? segment, string? product)
    {
        var venue = segment?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(venue))
        {
            return null; // no segment: read as the row it looks like; a Dhan answer without segments is not expected
        }

        if (venue != "NSE_EQ")
        {
            return venue switch
            {
                "BSE_EQ" => BrokerSkipReasons.Bse,
                _ when venue.Contains("FNO", StringComparison.Ordinal) || venue.Contains("FUT", StringComparison.Ordinal) || venue.Contains("OPT", StringComparison.Ordinal) => BrokerSkipReasons.Derivatives,
                _ when venue.Contains("CURRENCY", StringComparison.Ordinal) => BrokerSkipReasons.Currency,
                _ when venue.Contains("COMM", StringComparison.Ordinal) => BrokerSkipReasons.Commodity,
                _ => BrokerSkipReasons.NonEquity,
            };
        }

        return product is not null && IntradayProducts.Contains(product.Trim().ToUpperInvariant(), StringComparer.Ordinal) ? BrokerSkipReasons.Intraday : null;
    }
}
