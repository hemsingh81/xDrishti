using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>Rules for a trade of a tradebook or of the owner's hand, and how securities are named (ADR 0023). Pure.</summary>
public static class TradeRules
{
    /// <summary>The earliest trade date accepted (the day before it must still be a valid acquisition date, for the opening lot).</summary>
    public static readonly DateOnly EarliestDate = HoldingRules.EarliestDate.AddDays(1);

    /// <summary>The most shares one trade may have.</summary>
    public const long MaxQty = 1_000_000_000_000;

    /// <summary>The highest price per share accepted.</summary>
    public const decimal MaxPrice = 10_000_000m;

    /// <summary>The highest charges of one trade accepted.</summary>
    public const decimal MaxCharges = 1_000_000_000m;

    /// <summary>Prices and charges are stored with this many decimals; more are rounded away from zero.</summary>
    public const int Decimals = 4;

    public const int MaxExternalIdLength = 100;

    private const string ExchangePrefix = "NSE:";

    private const string SeriesSuffix = "-EQ";

    /// <summary>Adds a field error unless the date is between <see cref="EarliestDate"/> and <paramref name="today"/> (India).</summary>
    public static void CheckDate(FieldErrors errors, string field, DateOnly date, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (date > today)
        {
            errors.Add(field, "Cannot be in the future.");
        }
        else if (date < EarliestDate)
        {
            errors.Add(field, $"Cannot be before {EarliestDate:yyyy-MM-dd}.");
        }
    }

    public static string? QtyProblem(long qty) => qty switch
    {
        <= 0 => "Must be above 0.",
        > MaxQty => $"At most {MaxQty:N0}.",
        _ => null,
    };

    public static string? PriceProblem(decimal price) => price switch
    {
        <= 0m => "Must be above 0.",
        > MaxPrice => $"At most {MaxPrice:N0}.",
        _ => null,
    };

    public static string? ChargesProblem(decimal charges) => charges switch
    {
        < 0m => "Cannot be negative.",
        > MaxCharges => $"At most {MaxCharges:N0}.",
        _ => null,
    };

    public static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>buy · b · purchase · bought → buy; sell · s · sale · sold → sell (case-insensitive).</summary>
    public static bool TryParseSide(string? text, out TradeSide side)
    {
        switch (text?.Trim().ToUpperInvariant())
        {
            case "BUY" or "B" or "PURCHASE" or "BOUGHT":
                side = TradeSide.Buy;
                return true;
            case "SELL" or "S" or "SALE" or "SOLD":
                side = TradeSide.Sell;
                return true;
            default:
                side = default;
                return false;
        }
    }

    /// <summary>The trading symbol as holdings and trades compare it: trimmed, upper case, without an <c>NSE:</c> prefix and without a <c>-EQ</c> series suffix. Empty when there is none.</summary>
    public static string NormaliseSymbol(string? text)
    {
        var symbol = (text ?? string.Empty).Trim().ToUpperInvariant();
        if (symbol.StartsWith(ExchangePrefix, StringComparison.Ordinal))
        {
            symbol = symbol[ExchangePrefix.Length..].TrimStart();
        }

        if (symbol.EndsWith(SeriesSuffix, StringComparison.Ordinal) && symbol.Length > SeriesSuffix.Length)
        {
            symbol = symbol[..^SeriesSuffix.Length].TrimEnd();
        }

        return symbol;
    }

    /// <summary>The ISIN, trimmed and upper case; null when there is none.</summary>
    public static string? NormaliseIsin(string? text)
    {
        var isin = text?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(isin) ? null : isin;
    }
}
