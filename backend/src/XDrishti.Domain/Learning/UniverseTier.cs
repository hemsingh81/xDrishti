namespace XDrishti.Domain.Learning;

/// <summary>The liquidity tier of a share on one decision date (BK-11, ADR 0261).</summary>
public enum UniverseTier
{
    /// <summary>Too few eligible shares to rank (fewer than the options' minimum): no tier is invented from a tiny universe.</summary>
    Unavailable,

    /// <summary>The share has too few daily bars, or is not in the tracked set that date: it is in the <c>*</c> cell only.</summary>
    Unknown,

    /// <summary>The top third by median traded value.</summary>
    High,

    /// <summary>The middle third.</summary>
    Mid,

    /// <summary>The bottom third.</summary>
    Low,
}

/// <summary>The cell-key part of a tier.</summary>
public static class UniverseTierNames
{
    /// <summary>The seventh key part of a ranked tier; null for <see cref="UniverseTier.Unknown"/> and <see cref="UniverseTier.Unavailable"/>, which belong to no tier cell.</summary>
    public static string? CellPart(this UniverseTier tier) => tier switch
    {
        UniverseTier.High => "liq_high",
        UniverseTier.Mid => "liq_mid",
        UniverseTier.Low => "liq_low",
        _ => null,
    };
}
