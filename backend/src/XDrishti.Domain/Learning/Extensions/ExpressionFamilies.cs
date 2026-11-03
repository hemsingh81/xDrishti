namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The ledger families of the expression study (P14-09, protocol O): <c>expr:option_buy</c>. A family list of its own, apart from <see cref="ExtensionFamilies.All"/> (which the P15 phase check iterates) and from <see cref="PairFamilies.All"/>: the shared route lists all of them, the P15 phase check only its own.
/// P14-10, P14-13 and P14-15 add their families here or beside it. Names only; the protocol is in <c>XDrishti.Domain.Options.Evaluation</c>.
/// </summary>
public static class ExpressionFamilies
{
    /// <summary>A long option bought in place of the cash trade, on the same rupee risk.</summary>
    public const string OptionBuy = "expr:option_buy";

    /// <summary>The protocol letter of the records.</summary>
    public const string Protocol = "O";

    /// <summary>The version key of the records: the hold-out is read once per family and this key (one reading for the whole family, whatever the profile).</summary>
    public const string VersionKey = "expr-1";

    /// <summary>The expression families.</summary>
    public static IReadOnlyList<string> All { get; } = [OptionBuy];

    /// <summary>Whether the text names an expression family.</summary>
    public static bool IsKnown(string? family) => family is not null && All.Contains(family, StringComparer.Ordinal);
}
