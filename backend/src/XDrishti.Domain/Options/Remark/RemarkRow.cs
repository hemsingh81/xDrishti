namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// One row of an expression run (spec P14-08): the fate of one cash trade and, when it is <see cref="RemarkFates.Paired"/>, its option version. An unpaired row carries no premium, no units, no rupees and no R. Times are the option bar times of a paired row and
/// the cash times of an unpaired one; <see cref="ExitReason"/> is the cash word, <c>premium_stop</c> or <c>expiry_exit</c>. <see cref="Sizable"/> is the lot-feasibility word (a column, never a fate).
/// </summary>
public sealed record RemarkRow(
    string TradeKey,
    string Fate,
    string OptionSide,
    char? ExpiryFlag,
    int? ExpiryCode,
    DateOnly? Expiry,
    string ExpirySource,
    int? StrikePx,
    DateTimeOffset EntryTs,
    DateTimeOffset ExitTs,
    int? EntryPremiumPx,
    int? ExitPremiumPx,
    double? Units,
    decimal? GrossInr,
    decimal? NetInr,
    double? REquiv,
    double? CashR,
    string ExitReason,
    int? Lot,
    string Sizable,
    IReadOnlyList<string> Labels)
{
    public const string NotApplicable = "n/a";

    public bool IsPaired => Fate == RemarkFates.Paired;
}
