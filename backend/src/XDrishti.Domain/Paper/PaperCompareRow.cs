namespace XDrishti.Domain.Paper;

/// <summary>One stored paper entry of an epoch with its exit, as the comparison reads it (spec P10-04). Plain data.</summary>
/// <param name="EntryId">The entry id.</param>
/// <param name="PlanItemId">The ticket id (plan item).</param>
/// <param name="TicketGroup">primary or reserve.</param>
/// <param name="MeasureStatus">measured or straddle.</param>
/// <param name="CellKey">The cell key.</param>
/// <param name="TradeKey">The count-once key of the ticket.</param>
/// <param name="EntryAt">The entry instant.</param>
/// <param name="ExitAt">The exit instant; null while open.</param>
/// <param name="RSim">The trade's R; null while open.</param>
/// <param name="RBasis">net or gross; null while open.</param>
/// <param name="ChargeSheetId">The sheet that charged the exit; null without one.</param>
/// <param name="ApprovalKind">owner or auto.</param>
public sealed record PaperCompareRow(
    long EntryId,
    long PlanItemId,
    string TicketGroup,
    string MeasureStatus,
    string CellKey,
    string TradeKey,
    DateTimeOffset EntryAt,
    DateTimeOffset? ExitAt,
    double? RSim,
    string? RBasis,
    string? ChargeSheetId,
    string ApprovalKind = "owner");
