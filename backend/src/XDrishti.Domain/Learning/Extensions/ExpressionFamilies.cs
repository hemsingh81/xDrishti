namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The ledger families of the expression study (P14-09, protocol O): <c>expr:option_buy</c>. A family list of its own, apart from <see cref="ExtensionFamilies.All"/> (which the P15 phase check iterates) and from <see cref="PairFamilies.All"/>: the shared route lists all of them, the P15 phase check only its own.
/// P14-10 added the three structure families here (the condor under protocol R); P14-13 and P14-15 add theirs here or beside it. Names only; the protocol is in <c>XDrishti.Domain.Options.Evaluation</c>.
/// </summary>
public static class ExpressionFamilies
{
    /// <summary>A long option bought in place of the cash trade, on the same rupee risk.</summary>
    public const string OptionBuy = "expr:option_buy";

    /// <summary>A debit spread in place of the cash trade (P14-10), its own family, ledger count and verdict.</summary>
    public const string DebitSpread = "expr:debit_spread";

    /// <summary>A credit spread in place of an index cash trade, only in a stored range regime (P14-10).</summary>
    public const string CreditSpread = "expr:credit_spread";

    /// <summary>An iron condor of the index range family: no cash twin (P14-10).</summary>
    public const string IronCondor = "expr:iron_condor";

    /// <summary>The protocol letter of the records of the families that are judged against their cash trade (protocol O).</summary>
    public const string Protocol = "O";

    /// <summary>The protocol letter of the condor family, which has no cash twin and is judged on its own out-of-sample R.</summary>
    public const string CondorProtocol = "R";

    /// <summary>The version key of the records: the hold-out is read once per family and this key (one reading for the whole family, whatever the profile).</summary>
    public const string VersionKey = "expr-1";

    /// <summary>The expression families.</summary>
    public static IReadOnlyList<string> All { get; } = [OptionBuy, DebitSpread, CreditSpread, IronCondor];

    /// <summary>The protocol letter of a family: <c>R</c> for the condor, <c>O</c> for the others.</summary>
    public static string ProtocolOf(string family) => family == IronCondor ? CondorProtocol : Protocol;

    /// <summary>Whether the text names an expression family.</summary>
    public static bool IsKnown(string? family) => family is not null && All.Contains(family, StringComparer.Ordinal);
}
