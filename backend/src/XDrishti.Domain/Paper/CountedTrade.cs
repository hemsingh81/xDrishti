namespace XDrishti.Domain.Paper;

/// <summary>A paper trade that counts (spec P10-04, criterion 1).</summary>
/// <param name="CellKey">The cell key.</param>
/// <param name="R">The trade's R.</param>
/// <param name="RBasis">net or gross.</param>
/// <param name="ChargeSheetId">The sheet that charged it, or null.</param>
/// <param name="EntryAt">The entry instant.</param>
/// <param name="ExitAt">The exit instant.</param>
/// <param name="TicketId">The ticket (plan item); the phase check proves one ticket counts once (spec P10-06).</param>
/// <param name="ApprovalKind">owner or auto (spec P10-06 prints the auto split).</param>
public sealed record CountedTrade(string CellKey, double R, string RBasis, string? ChargeSheetId, DateTimeOffset EntryAt, DateTimeOffset ExitAt, long TicketId = 0, string ApprovalKind = "owner");
