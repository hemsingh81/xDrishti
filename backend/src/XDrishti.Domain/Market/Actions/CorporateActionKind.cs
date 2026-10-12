namespace XDrishti.Domain.Market.Actions;

/// <summary>
/// What kind of corporate action changes the share count and so the price level (ADR 0017 §5). Dividends are not adjusted (config <c>adjust.dividends: false</c>).
/// Stored by lower-case name — never rename a value.
/// </summary>
public enum CorporateActionKind
{
    /// <summary>A split or consolidation <c>numerator:denominator</c> = old shares : new shares (1:5 turns one share into five). Price factor <c>numerator / denominator</c>.</summary>
    Split,

    /// <summary>A bonus issue <c>numerator:denominator</c> = bonus shares : shares held (1:1 gives one new share for each held). Price factor <c>denominator / (numerator + denominator)</c>.</summary>
    Bonus,
}

/// <summary>The codes of <see cref="CorporateActionKind"/> used in the database and the API: <c>split</c> · <c>bonus</c>.</summary>
public static class CorporateActionKindCodes
{
    public const string Split = "split";
    public const string Bonus = "bonus";

    public static string ToCode(this CorporateActionKind kind) => kind switch
    {
        CorporateActionKind.Split => Split,
        CorporateActionKind.Bonus => Bonus,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown corporate action kind."),
    };

    public static bool TryParse(string? code, out CorporateActionKind kind)
    {
        switch (code)
        {
            case Split:
                kind = CorporateActionKind.Split;
                return true;
            case Bonus:
                kind = CorporateActionKind.Bonus;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
