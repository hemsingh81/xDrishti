using System.Text.Json;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Turns one page of Dhan's trade history (ADR 0025, <c>GET /v2/trades/{from}/{to}/{page}</c>) into NSE equity trades, read against Dhan's published sample. A row is read by <c>exchangeTradeId</c> (else <c>tradeId</c>), <c>orderId</c>, <c>transactionType</c> (buy or sell),
/// <c>exchangeSegment</c>, <c>productType</c>, <c>tradingSymbol</c> (which can be <c>null</c>), <c>isin</c>, <c>securityId</c>, <c>tradedQuantity</c>, <c>tradedPrice</c>, the six charge fields (<c>sebiTax</c>, <c>stt</c>, <c>brokerageCharges</c>, <c>serviceTax</c>,
/// <c>exchangeTransactionCharges</c>, <c>stampDuty</c>: rupee amounts, summed into <see cref="BrokerTrade.Charges"/>) and the time <c>exchangeTime</c> (else <c>createTime</c>, else <c>updateTime</c>; <c>"NA"</c> counts as missing), India time; names match in any case and
/// numbers may be text (<see cref="DhanJson"/>). <c>customSymbol</c> is a company name, never a ticker. A row without a ticker is kept when it has an ISIN or a security id (the use case finds the share in the instrument master); a row with none of the three is skipped as
/// <see cref="BrokerSkipReasons.NoSymbol"/>. Only <c>NSE_EQ</c> rows that are not intraday products are kept (delivery, <c>MTF</c>, <c>MARGIN</c>): other segments are skipped and counted per reason (<see cref="BrokerSkipReasons"/>), so are intraday, cover and bracket orders, which net out
/// within the day and never reach the holdings. A row without an id, a side, a whole positive quantity, a positive price or a readable date is dropped and counted <b>with its reason</b> (<see cref="BrokerDropReasons"/>: the first problem in that order) and the index of the first few in the page; nothing of the row's content is kept. Pure.
/// </summary>
internal static class DhanTradeMapper
{
    /// <summary>
    /// The version of this mapping (ADR 0027). <b>Raise it with every change that lets a row be read that was dropped or skipped before</b> (a field name, the symbol resolution, a product that was unmapped): the next read of each account then reads
    /// the windows that dropped rows for a data reason again, which the identities make safe. 1: the mapping of ADR 0025 amendments a to h.
    /// </summary>
    public const int Version = 1;

    /// <summary>The longest trade id kept (the identity is the date and the id).</summary>
    public const int MaxTradeIdLength = 64;

    private const decimal Limit = 1_000_000_000_000m;

    /// <summary>
    /// Products whose trades net out within the day and never reach the holdings. <c>MTF</c> (margin trading funding) and <c>CNC</c> are delivery and stay. <c>MARGIN</c> is not listed: Dhan documents it as the carry-forward product of futures and
    /// options, which are skipped by their segment, so on <c>NSE_EQ</c> it can only be a position carried overnight - a trade the holdings may contain - and skipping it would leave shares out of the reconstruction (a quantity mismatch that
    /// stops the rebuild), while storing an intraday pair that nets to zero costs nothing.
    /// </summary>
    private static readonly string[] IntradayProducts = ["INTRADAY", "INTRA", "MIS", "CO", "BO"];

    private static readonly string[] ChargeFields = ["sebiTax", "stt", "brokerageCharges", "serviceTax", "exchangeTransactionCharges", "stampDuty"];

    private static readonly string[] TimeFields = ["exchangeTime", "createTime", "updateTime"];

    public static BrokerTradePage Map(JsonElement rows)
    {
        var trades = new List<BrokerTrade>(rows.ValueKind == JsonValueKind.Array ? rows.GetArrayLength() : 0);
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var drops = new List<BrokerRowDrop>();
        var count = 0;
        if (rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var result = Read(row);
                if (result.Trade is { } trade)
                {
                    trades.Add(trade);
                }
                else if (result.Skip is { } reason)
                {
                    skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                }
                else
                {
                    drops.Add(new BrokerRowDrop(count, result.Drop ?? BrokerDropReasons.Other));
                }

                count++;
            }
        }

        return new BrokerTradePage(trades, count, drops.Count, skipped) { Drops = BrokerReadDrops.From(drops) };
    }

    /// <summary>What a row is: a trade, a skipped row (with its <see cref="BrokerSkipReasons"/> reason) or a dropped one (with its <see cref="BrokerDropReasons"/> reason). Exactly one is set.</summary>
    private readonly record struct RowResult(BrokerTrade? Trade, string? Skip, string? Drop)
    {
        public static RowResult Dropped(string reason) => new(null, null, reason);
    }

    private static RowResult Read(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return RowResult.Dropped(BrokerDropReasons.UnreadableRow);
        }

        if (SkipReason(DhanJson.Text(row, "exchangeSegment", "segment"), DhanJson.Text(row, "productType", "product")) is { } skip)
        {
            return new RowResult(null, skip, null);
        }

        // The first problem found is the reason; the order is the order of the checks.
        var id = DhanJson.Text(row, "exchangeTradeId", "tradeId");
        if (id is null)
        {
            return RowResult.Dropped(BrokerDropReasons.MissingId);
        }

        if (id.Length > MaxTradeIdLength)
        {
            return RowResult.Dropped(BrokerDropReasons.BadId);
        }

        if (!TradeRules.TryParseSide(DhanJson.Text(row, "transactionType", "side"), out var side))
        {
            return RowResult.Dropped(BrokerDropReasons.MissingSide);
        }

        if (DhanJson.Number(row, Limit, "tradedQuantity", "quantity") is not { } quantity || quantity <= 0m || quantity != decimal.Truncate(quantity))
        {
            return RowResult.Dropped(BrokerDropReasons.BadQuantity);
        }

        if (DhanJson.Number(row, Limit, "tradedPrice", "price") is not { } traded || traded <= 0m)
        {
            return RowResult.Dropped(BrokerDropReasons.BadPrice);
        }

        if (Moment(row) is not { } at)
        {
            return RowResult.Dropped(BrokerDropReasons.BadTime);
        }

        // The ticker can be null in Dhan's answers (the published sample has "tradingSymbol": null); "customSymbol" is a company name and is never used as one. The ISIN and the security id let the use case find the share.
        var symbol = DhanJson.Known(row, "tradingSymbol");
        var isin = Isin(DhanJson.Known(row, "isin"));
        var securityId = DhanJson.Known(row, "securityId");
        if (symbol is null && isin is null && securityId is null)
        {
            return new RowResult(null, BrokerSkipReasons.NoSymbol, null);
        }

        return new RowResult(
            new BrokerTrade(id, DhanJson.Known(row, "orderId", "exchangeOrderId"), side, symbol, isin, securityId, (long)quantity, traded, Charges(row), at.Date, at.Time),
            null,
            null);
    }

    /// <summary>The first of the exchange, create and update times that reads ("NA" and blanks do not); India time.</summary>
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

    /// <summary>The sum of Dhan's six charge fields (rupee amounts, as numbers or text; a field that is missing, "NA", negative or not a number counts as 0), rounded to 4 decimals; never negative.</summary>
    private static decimal Charges(JsonElement row)
    {
        var total = 0m;
        foreach (var name in ChargeFields)
        {
            if (DhanJson.Number(row, Limit, name) is > 0m and var amount)
            {
                total += amount;
            }
        }

        total = TradeRules.Round(total);
        return total <= TradeRules.MaxCharges ? total : 0m;
    }

    /// <summary>The ISIN in upper case when it has the shape of one (2 letters, 9 letters or digits, a check digit); null otherwise.</summary>
    private static string? Isin(string? text)
    {
        var code = text?.Trim().ToUpperInvariant();
        return code is { Length: 12 } && char.IsAsciiLetterUpper(code[0]) && char.IsAsciiLetterUpper(code[1]) && code[2..11].All(char.IsAsciiLetterOrDigit) && char.IsAsciiDigit(code[11]) ? code : null;
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
