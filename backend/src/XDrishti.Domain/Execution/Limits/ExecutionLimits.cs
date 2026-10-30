using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Execution.Limits;

/// <summary>
/// One account's limits sheet (spec P12-09): the hard caps every system-sent entry must stay inside, in <c>semi_auto</c> as in <c>auto_live</c>. A null cap is unset, and an unset cap, an empty allow-list, an empty product list or no window
/// allows nothing (D3: an empty sheet allows nothing; there are no "reasonable" defaults the owner never chose). The allow-list holds NSE trading symbols as the instrument master spells them, matched exactly (<c>TATAPOWER</c> is not
/// <c>TATAPOWER-EQ</c>). Pure.
/// </summary>
public sealed record ExecutionLimits(
    int? MaxOrdersPerDay,
    decimal? MaxNotionalPerOrder,
    decimal? MaxNotionalPerDay,
    decimal? MaxLossPerDay,
    int? MaxOpenPositions,
    IReadOnlyList<string> AllowList,
    TimeOnly? WindowStart,
    TimeOnly? WindowEnd,
    IReadOnlyList<string> Products,
    bool ExitAllOnLossHalt)
{
    public const string FieldOrders = "maxOrdersPerDay";
    public const string FieldNotionalOrder = "maxNotionalPerOrder";
    public const string FieldNotionalDay = "maxNotionalPerDay";
    public const string FieldLoss = "maxLossPerDay";
    public const string FieldPositions = "maxOpenPositions";
    public const string FieldAllowList = "allowList";
    public const string FieldWindow = "window";
    public const string FieldProducts = "products";

    private const decimal MaxMoney = 9_999_999_999_999_999.99m; // numeric(18,2)

    /// <summary>The empty sheet: everything unset. It allows nothing.</summary>
    public static ExecutionLimits Empty { get; } = new(null, null, null, null, null, [], null, null, [], false);

    /// <summary>The fields that are unset, in the order of the form. An empty list means complete.</summary>
    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        AddIf(missing, MaxOrdersPerDay is null, FieldOrders);
        AddIf(missing, MaxNotionalPerOrder is null, FieldNotionalOrder);
        AddIf(missing, MaxNotionalPerDay is null, FieldNotionalDay);
        AddIf(missing, MaxLossPerDay is null, FieldLoss);
        AddIf(missing, MaxOpenPositions is null, FieldPositions);
        AddIf(missing, AllowList.Count == 0, FieldAllowList);
        AddIf(missing, WindowStart is null || WindowEnd is null, FieldWindow);
        AddIf(missing, Products.Count == 0, FieldProducts);
        return missing;
    }

    public bool IsComplete => Missing().Count == 0;

    /// <summary>The first field whose value is outside the rules and ceilings, or null when every set value is legal. An unset value is legal (it allows nothing).</summary>
    public string? Invalid()
    {
        if (MaxOrdersPerDay is { } orders && orders is < 1 or > LimitsCeilings.MaxOrdersPerDay)
        {
            return FieldOrders;
        }

        if (MaxNotionalPerOrder is { } perOrder && !MoneyOk(perOrder))
        {
            return FieldNotionalOrder;
        }

        if (MaxNotionalPerDay is { } perDay && (!MoneyOk(perDay) || (MaxNotionalPerOrder is { } o && perDay < o)))
        {
            return FieldNotionalDay;
        }

        if (MaxLossPerDay is { } loss && !MoneyOk(loss))
        {
            return FieldLoss;
        }

        if (MaxOpenPositions is { } positions && positions is < 1 or > LimitsCeilings.MaxOpenPositions)
        {
            return FieldPositions;
        }

        if (AllowList.Count > LimitsCeilings.MaxAllowList || AllowList.Any(s => !IsTradingSymbol(s)) || AllowList.Distinct(StringComparer.Ordinal).Count() != AllowList.Count)
        {
            return FieldAllowList;
        }

        if ((WindowStart is null) != (WindowEnd is null)
            || (WindowStart is { } start && WindowEnd is { } end && (start < LimitsCeilings.WindowEarliest || end > LimitsCeilings.WindowLatest || start >= end || start.Second != 0 || end.Second != 0 || start.Millisecond != 0 || end.Millisecond != 0)))
        {
            return FieldWindow;
        }

        return Products.Any(p => !LimitsCeilings.Products.Contains(p)) || Products.Distinct(StringComparer.Ordinal).Count() != Products.Count ? FieldProducts : null;
    }

    /// <summary>True when the trading symbol of <paramref name="symbol"/> (a canonical <c>NSE:</c> symbol or a bare trading symbol) is on the allow-list, by exact, case-sensitive match.</summary>
    public bool Allows(string symbol) => AllowList.Contains(TradingSymbol(symbol), StringComparer.Ordinal);

    /// <summary>The trading symbol of a canonical symbol: the part after <c>NSE:</c>; any other text is returned as it is.</summary>
    public static string TradingSymbol(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return symbol.StartsWith("NSE:", StringComparison.Ordinal) ? symbol[4..] : symbol;
    }

    /// <summary>SHA-256 of the canonical text of the sheet (sorted lists, invariant numbers): equal sheets give an equal hash.</summary>
    public string Hash()
    {
        var text = new StringBuilder("v1");
        foreach (var part in new[]
        {
            Num(MaxOrdersPerDay), Money(MaxNotionalPerOrder), Money(MaxNotionalPerDay), Money(MaxLossPerDay), Num(MaxOpenPositions),
            string.Join(',', AllowList.Order(StringComparer.Ordinal)), Time(WindowStart), Time(WindowEnd), string.Join(',', Products.Order(StringComparer.Ordinal)), ExitAllOnLossHalt ? "1" : "0",
        })
        {
            text.Append('|').Append(part);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static bool MoneyOk(decimal value) => value > 0m && value <= MaxMoney && decimal.Round(value, 2) == value;

    private static void AddIf(List<string> list, bool condition, string field)
    {
        if (condition)
        {
            list.Add(field);
        }
    }

    private static string Num(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Money(decimal? v) => v?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-";

    private static string Time(TimeOnly? v) => v?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "-";

    /// <summary>An NSE trading symbol as the instrument master spells it: upper-case letters, digits, '&amp;' and '-' (M&amp;M, BAJAJ-AUTO), starting with a letter or digit, at most 30 characters (the database checks the same pattern).</summary>
    public static bool IsTradingSymbol(string? symbol) =>
        symbol is { Length: >= 1 and <= 30 } s && (char.IsAsciiLetterUpper(s[0]) || char.IsAsciiDigit(s[0])) && s.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '&' or '-');
}
