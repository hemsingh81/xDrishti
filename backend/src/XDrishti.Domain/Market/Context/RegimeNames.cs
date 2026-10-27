namespace XDrishti.Domain.Market.Context;

/// <summary>
/// The nine market regimes of doc 07 §6 (trend × volatility) by code and name (P6-02, ADR 0146). The Domain cannot reference the indicator library (ADR 0039), so the spelling is kept here and a test in the indicator test project fails when it differs
/// from <c>Regime.Name</c>. A regime cell's regime part is the name, followed by <c>~index_only</c> when the label was built from the index alone (<see cref="ContextBasis.IndexOnly"/>).
/// </summary>
public static class RegimeNames
{
    /// <summary>The suffix of a regime cell part built from the index alone (the VIX percentile was not ready).</summary>
    public const string IndexOnlySuffix = "~index_only";

    private static readonly string[] Labels =
    [
        "UP_LOW_VOL", "UP_NORMAL_VOL", "UP_HIGH_VOL",
        "RANGE_LOW_VOL", "RANGE_NORMAL_VOL", "RANGE_HIGH_VOL",
        "DOWN_LOW_VOL", "DOWN_NORMAL_VOL", "DOWN_HIGH_VOL",
    ];

    /// <summary>The nine names in the order of their codes 1 to 9.</summary>
    public static IReadOnlyList<string> All => Labels;

    /// <summary>The name of a code from 1 to 9; any other code throws (an unavailable label has no code and must be checked first).</summary>
    public static string NameOf(int code) =>
        code is >= 1 and <= 9 ? Labels[code - 1] : throw new ArgumentOutOfRangeException(nameof(code), code, "A regime code is 1 to 9.");

    /// <summary>The code of a name, or null when it is none of the nine (the spelling is exact and case-sensitive).</summary>
    public static int? CodeOf(string? name)
    {
        var index = Array.IndexOf(Labels, name);
        return index < 0 ? null : index + 1;
    }

    /// <summary>Reads a regime cell part back into its name and basis; null for <c>*</c> or anything that is not a regime part.</summary>
    public static (string Name, string Basis)? ParsePart(string? part)
    {
        if (string.IsNullOrEmpty(part))
        {
            return null;
        }

        var indexOnly = part.EndsWith(IndexOnlySuffix, StringComparison.Ordinal);
        var name = indexOnly ? part[..^IndexOnlySuffix.Length] : part;
        return CodeOf(name) is null ? null : (name, indexOnly ? ContextBasis.IndexOnly : ContextBasis.IndexAndVix);
    }
}
