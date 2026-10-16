using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Which column of a dividend payout file is which, found by the header names (ADR 0028): case and punctuation do not matter (<c>Dividend Paid</c>, <c>dividend_paid</c> and <c>DIVIDENDPAID</c> are one name). Required: a date, a scrip name (or a symbol or an
/// ISIN) and the amount paid. Optional: the dividend per share, the quantity, the symbol and the ISIN beside the name. Unknown columns are ignored. The aliases of a column are in order of preference. A plain amount (<c>Amount</c>,
/// <c>Net Amount</c>, <c>Total Amount</c>) counts as the amount paid only beside a header that says dividend (one that contains dividend, per share or payout, or is DPS): a date, a name and an unlabelled amount are a statement, not a dividend list.
/// A file with a buy/sell column (<c>Side</c>, <c>Trade Type</c>, <c>Transaction Type</c>, ...) is a tradebook and never a dividend list.
/// </summary>
internal sealed class DividendColumns
{
    private static readonly string[] DateAliases = ["date", "paydate", "paymentdate", "payoutdate", "dividenddate", "datepaid", "creditdate", "datecredited", "creditedon", "paidon"];

    private static readonly string[] NameAliases = ["scripname", "scrip", "companyname", "company", "securityname", "security", "stockname", "stock", "instrumentname", "instrument", "name", "script", "scriptname"];

    private static readonly string[] SymbolAliases = ["symbol", "tradingsymbol", "ticker", "tsym", "scripsymbol", "stocksymbol"];

    private static readonly string[] IsinAliases = ["isin", "isincode", "isinno"];

    /// <summary>Headers that say it is a dividend amount.</summary>
    private static readonly string[] ExplicitAmountAliases =
    [
        "dividendpaid", "dividendamount", "dividendreceived", "dividendcredited", "netdividend", "totaldividend", "amountpaid", "paidamount", "amountreceived", "payoutamount", "netpayout", "payout", "paid", "received",
    ];

    /// <summary>Headers that name an amount without saying what for (accepted only beside a header that says dividend, see <see cref="SaysDividend"/>).</summary>
    private static readonly string[] PlainAmountAliases = ["netamount", "totalamount", "amount"];

    private static readonly string[] PerShareAliases = ["dividendpershare", "pershare", "dps", "amountpershare", "dividendperunit", "dividendrate", "perunit", "rate"];

    private static readonly string[] QtyAliases = ["quantity", "qty", "shares", "noofshares", "numberofshares", "sharesheld", "eligibleshares", "eligiblequantity", "units", "holdingquantity"];

    private DividendColumns()
    {
    }

    public int Date { get; private init; } = -1;

    public int Name { get; private init; } = -1;

    public int Symbol { get; private init; } = -1;

    public int Isin { get; private init; } = -1;

    public int Amount { get; private init; } = -1;

    public int PerShare { get; private init; } = -1;

    public int Qty { get; private init; } = -1;

    /// <summary>What the header has of a dividend file, without reading the rest (the file-kind detector uses it).</summary>
    public static DividendColumnProbe Probe(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var names = header.Select(TradeColumns.Normalise).ToArray();
        var amount = AmountOf(names, out var plain);
        return new DividendColumnProbe(
            Pick(names, DateAliases) >= 0,
            Pick(names, NameAliases) >= 0 || Pick(names, SymbolAliases) >= 0 || Pick(names, IsinAliases) >= 0,
            amount >= 0,
            plain);
    }

    /// <summary>
    /// Finds the columns. Failure (<c>portfolio.invalid</c>, field <c>file</c>) when a required column is missing, naming what is missing and the accepted names, when the amount is a plain one with nothing in the header that says dividend, or when the
    /// file has a buy/sell column.
    /// </summary>
    public static Result<DividendColumns> Find(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var names = header.Select(TradeColumns.Normalise).ToArray();
        if (TradeColumns.Probe(header).Side)
        {
            return Result.Failure<DividendColumns>(PortfolioErrors.Invalid(
                "file", "The file has a buy/sell column (Side, Trade Type, Transaction Type), which a dividend list does not have. If it is a tradebook, import it as trades on the Portfolio screen."));
        }

        var date = Pick(names, DateAliases);
        var name = Pick(names, NameAliases);
        var symbol = Pick(names, SymbolAliases);
        var isin = Pick(names, IsinAliases);
        var amount = AmountOf(names, out _);
        var missing = new List<string>();
        if (date < 0)
        {
            missing.Add("date");
        }

        if (name < 0 && symbol < 0 && isin < 0)
        {
            missing.Add("scrip name");
        }

        if (amount < 0)
        {
            missing.Add("amount paid");
        }

        if (missing.Count > 0)
        {
            var unlabelled = amount < 0 && Pick(names, PlainAmountAliases) >= 0
                ? " The Amount column is not taken as dividends because nothing in the header says dividend: name it Dividend Paid, or add a Dividend Per Share column beside it."
                : string.Empty;
            return Result.Failure<DividendColumns>(PortfolioErrors.Invalid(
                "file",
                $"The file has no column for {string.Join(", ", missing)}. A dividend file needs a date (Date, Pay Date), a scrip name (Scrip Name, Company, Name; or a Symbol or ISIN) and the amount paid (Dividend Paid, Dividend Amount, Payout; "
                + "or Amount beside a Dividend Per Share column); a dividend per share and a quantity are optional - for example: Date, Scrip Name, Dividend Per Share, Quantity, Dividend Paid. Header names are accepted in any case, punctuation ignored."
                + unlabelled));
        }

        return Result.Success(new DividendColumns
        {
            Date = date,
            Name = name,
            Symbol = symbol,
            Isin = isin,
            Amount = amount,
            PerShare = Pick(names, PerShareAliases),
            Qty = Pick(names, QtyAliases),
        });
    }

    /// <summary>True when a header names dividends: it contains dividend, per share or payout, or is DPS.</summary>
    private static bool SaysDividend(string[] names) =>
        names.Any(n => n.Contains("dividend", StringComparison.Ordinal) || n.Contains("pershare", StringComparison.Ordinal) || n.Contains("payout", StringComparison.Ordinal) || n == "dps");

    /// <summary>The amount paid: an explicit dividend amount header, else a plain amount header when another header says dividend; -1 when neither. <paramref name="unlabelled"/> is true when the file has a plain amount that nothing makes a dividend amount.</summary>
    private static int AmountOf(string[] names, out bool unlabelled)
    {
        var explicitAmount = Pick(names, ExplicitAmountAliases);
        var plain = Pick(names, PlainAmountAliases);
        if (explicitAmount >= 0)
        {
            unlabelled = false;
            return explicitAmount;
        }

        if (plain >= 0 && SaysDividend(names))
        {
            unlabelled = false;
            return plain;
        }

        unlabelled = plain >= 0;
        return -1;
    }

    private static int Pick(string[] names, string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var index = Array.IndexOf(names, alias);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>
/// What a header has of a dividend file: a date, a name (or symbol or ISIN) and a dividend amount (a header that says dividend, or a plain amount beside one). <c>HasUnlabelledAmount</c>: it has a plain amount (Amount, Net Amount) that nothing
/// in the header makes a dividend amount.
/// </summary>
internal readonly record struct DividendColumnProbe(bool Date, bool Name, bool Amount, bool HasUnlabelledAmount)
{
    public bool IsDividendFile => Date && Name && Amount;

    /// <summary>A date, a name and a plain amount that nothing says is a dividend.</summary>
    public bool IsStatement => Date && Name && HasUnlabelledAmount;
}
