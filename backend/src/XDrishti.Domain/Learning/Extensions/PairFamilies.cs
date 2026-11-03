namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The ledger family of the pairs study (P14-13, protocol S): <c>pairs:intraday-1</c>. A family list of its own, apart from <see cref="ExtensionFamilies.All"/> (which the P15 phase check iterates): the shared route lists both, the P15 phase check only its own.
/// Names only; the pairs logic is in <c>XDrishti.Domain.PairResearch</c>.
/// </summary>
public static class PairFamilies
{
    /// <summary>Same-sector cash pairs formed on 252 daily closes and traded intraday on 5-minute spreads.</summary>
    public const string Intraday = "pairs:intraday-1";

    /// <summary>The protocol letter of the records.</summary>
    public const string Protocol = "S";

    /// <summary>The version key of the records: the hold-out is read once per family and this key.</summary>
    public const string VersionKey = "pairs-1";

    /// <summary>The families of the pairs study.</summary>
    public static IReadOnlyList<string> All { get; } = [Intraday];

    /// <summary>Whether the text names a pairs family.</summary>
    public static bool IsKnown(string? family) => family is not null && All.Contains(family, StringComparer.Ordinal);
}
