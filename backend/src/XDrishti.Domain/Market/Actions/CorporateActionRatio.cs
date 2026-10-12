namespace XDrishti.Domain.Market.Actions;

/// <summary>
/// The ratio of a corporate action and the price factor it implies (ADR 0017 §5). Meaning of <c>numerator:denominator</c>:
/// <list type="bullet">
/// <item><b>split</b>: old shares : new shares. A 1:5 split turns one share into five, so older prices are multiplied by 1/5 = 0.2; a 5:1 consolidation gives 5.</item>
/// <item><b>bonus</b>: bonus shares : shares held ("a bonus shares for every b held"). A 1:1 bonus doubles the shares, so older prices are multiplied by b / (a + b) = 0.5; 1:2 gives 2/3.</item>
/// </list>
/// The factor is rounded to <see cref="FactorDecimals"/> decimals — the value that is stored and used.
/// </summary>
public static class CorporateActionRatio
{
    /// <summary>The largest numerator or denominator accepted.</summary>
    public const int MaxPart = 100_000;

    public const int FactorDecimals = 12;

    /// <summary>Whether a ratio can be a corporate action: both parts 1 to <see cref="MaxPart"/>, and not one that changes nothing (a 1:1 split).</summary>
    public static bool IsValid(CorporateActionKind kind, int numerator, int denominator) =>
        numerator is >= 1 and <= MaxPart && denominator is >= 1 and <= MaxPart && !(kind == CorporateActionKind.Split && numerator == denominator);

    /// <summary>The multiplier for the prices before the ex-date.</summary>
    public static decimal Factor(CorporateActionKind kind, int numerator, int denominator)
    {
        if (!IsValid(kind, numerator, denominator))
        {
            throw new ArgumentException("The ratio is not a corporate action.");
        }

        var raw = kind == CorporateActionKind.Split
            ? (decimal)numerator / denominator
            : (decimal)denominator / (numerator + denominator);
        return Math.Round(raw, FactorDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>The ratio as written, <c>numerator:denominator</c>.</summary>
    public static string Text(int numerator, int denominator) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{numerator}:{denominator}");
}
