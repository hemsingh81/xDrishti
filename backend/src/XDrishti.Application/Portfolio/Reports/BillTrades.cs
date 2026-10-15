using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>One side of a bill row as a trade: its quantity, its value, the price (value ÷ quantity to 6 decimals), the charges it carries and whether quantity × price gives the value back to the paisa.</summary>
internal sealed record BillLeg(GtrRow Row, TradeSide Side, long Qty, decimal Value, decimal Price, decimal Charges, bool Exact);

/// <summary>
/// How a bill row of the global transaction report becomes trades (ADR 0029). A row with a buy quantity gives a buy and one with a sell quantity a sell — a row with both (a same-day round trip) gives two, the buy first. The price is the value ÷ quantity to
/// 6 decimals; the charges are the row's seven charges, split between the two legs by value when there are two (the first leg rounded to 4 decimals, the second takes the rest, so the legs add up exactly); a row whose charges are a net refund carries none. The
/// identity of a leg is <c>g:</c> and 32 hex characters of the hash of the bill number, the day, the scrip's normalised name and the side: the same bill read again from another report is the same trade. Which rows are not equity trades is decided from the exchange and the
/// name (<c>FUT …</c>, <c>OPT …</c>). <b>Changing the name rules of <see cref="DividendNames"/> changes these identities</b>: <c>BillIdentityGoldenTests</c> pins them. Pure.
/// </summary>
internal static class BillTrades
{
    /// <summary>Why a row is not imported although it is valid, besides the reasons of <see cref="TradeSkips"/>.</summary>
    public const string CoveredByDhan = "covered_by_dhan";

    private const int HashLength = 32;

    public static IReadOnlyList<BillLeg> Legs(GtrRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var legs = new List<BillLeg>(2);
        var fees = Math.Max(0m, row.Charges);
        var total = row.BuyValue + row.SellValue;
        var buyShare = row.BuyQty > 0 && row.SellQty > 0 ? TradeRules.Round(fees * row.BuyValue / total) : row.BuyQty > 0 ? fees : 0m;
        if (row.BuyQty > 0)
        {
            legs.Add(Leg(row, TradeSide.Buy, row.BuyQty, row.BuyValue, buyShare));
        }

        if (row.SellQty > 0)
        {
            legs.Add(Leg(row, TradeSide.Sell, row.SellQty, row.SellValue, fees - buyShare));
        }

        return legs;
    }

    /// <summary>The reason a row is not an equity trade of the NSE or BSE (<see cref="TradeSkips"/>); null when it is one.</summary>
    public static string? NotEquity(GtrRow row, BillName name)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(name);
        var upper = name.Display.ToUpperInvariant();
        if (upper.StartsWith("FUT ", StringComparison.Ordinal) || upper.StartsWith("OPT ", StringComparison.Ordinal)
            || TradeSkips.LooksLikeDerivative(upper) || (name.SymbolHint is { } hint && TradeSkips.LooksLikeDerivative(hint)))
        {
            return TradeSkips.Derivatives;
        }

        var exchange = ReportCells.Norm(row.Exchange);
        return exchange switch
        {
            "" or "nse" or "bse" => null,
            "mcx" or "ncdex" => TradeSkips.Commodity,
            "cds" or "nsecds" or "bcd" or "bsecds" => TradeSkips.Currency,
            "nfo" or "bfo" or "fo" => TradeSkips.Derivatives,
            _ => TradeSkips.NonEquity,
        };
    }

    public static bool IsBse(GtrRow row) => ReportCells.Norm(row.Exchange) == "bse";

    /// <summary>True for a row of the NSE: of trades that share an identity, it keeps the plain one.</summary>
    public static bool KeepsPlainIdentity(GtrRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ReportCells.Norm(row.Exchange) == "nse";
    }

    /// <summary>True for a row that names an exchange other than the NSE (the BSE): the only kind of row whose identity is also salted by an NSE sibling that was left out of the plan (ADR 0030). A blank exchange is not one.</summary>
    public static bool IsOtherExchange(GtrRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var exchange = ReportCells.Norm(row.Exchange);
        return exchange.Length > 0 && exchange != "nse";
    }

    /// <summary>What a row adds to its identity when another trade of the file shares it: its exchange, or a fixed token when the exchange is blank (so a blank exchange never meets an NSE row's identity).</summary>
    public static string CollisionSalt(GtrRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var exchange = ReportCells.Norm(row.Exchange);
        return exchange.Length > 0 ? exchange : "unknown";
    }

    /// <summary>
    /// The identity of a bill-level trade. The exchange is <b>not</b> part of it (the stored identities must stay stable); only when another trade of the same file shares the identity - an NSE and a BSE bill with a number, scrip, day and side in common -
    /// <paramref name="collisionSalt"/> (<see cref="CollisionSalt"/>) is added to tell them apart, for every trade but the NSE one, whatever the order of the rows.
    /// </summary>
    public static string Identity(GtrRow row, TradeSide side, BillName name, string? collisionSalt = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(name);
        var text = string.Create(CultureInfo.InvariantCulture, $"{row.BillNo}|{row.Date:yyyyMMdd}|{name.Key}|{(side == TradeSide.Buy ? "buy" : "sell")}");
        if (!string.IsNullOrEmpty(collisionSalt))
        {
            text += "|" + ReportCells.Norm(collisionSalt);
        }

        return "g:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..HashLength];
    }

    private static BillLeg Leg(GtrRow row, TradeSide side, long qty, decimal value, decimal charges)
    {
        var price = TradeRules.RoundPrice(value / qty);
        return new BillLeg(row, side, qty, value, price, TradeRules.Round(charges), Math.Round(qty * price, 2, MidpointRounding.AwayFromZero) == Math.Round(value, 2, MidpointRounding.AwayFromZero));
    }
}
