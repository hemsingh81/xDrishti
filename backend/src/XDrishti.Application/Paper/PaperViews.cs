namespace XDrishti.Application.Paper;

/// <summary>The exit of a paper trade as the API shows it (spec P10-01).</summary>
/// <param name="ExitAt">The start of the exit minute.</param>
/// <param name="ExitPrice">The filled exit.</param>
/// <param name="ExitReason">Why the trade ended.</param>
/// <param name="HeldSessions">Sessions held, entry and exit session included.</param>
/// <param name="GrossPnl">Rupees before charges at the owner's quantity.</param>
/// <param name="Charges">Rupees of charges; null without a sheet.</param>
/// <param name="NetPnl">Gross less charges; null without a sheet.</param>
/// <param name="RExact">R from the exact prices.</param>
/// <param name="RSim">R of the trade: net when charged, else gross (the grade's R).</param>
/// <param name="RBasis">net or gross.</param>
/// <param name="MaeR">Maximum adverse excursion in R.</param>
/// <param name="MfeR">Maximum favourable excursion in R.</param>
public sealed record PaperExitView(DateTimeOffset ExitAt, decimal ExitPrice, string ExitReason, int HeldSessions, decimal GrossPnl, decimal? Charges, decimal? NetPnl, double RExact, double RSim, string RBasis, double MaeR, double MfeR);

/// <summary>One paper trade of one account. It is a replay: no order was placed or exists (<see cref="Basis"/>).</summary>
/// <param name="Id">The entry id.</param>
/// <param name="PlanId">The plan.</param>
/// <param name="PlanItemId">The ticket.</param>
/// <param name="AccountId">The paper account.</param>
/// <param name="EpochId">The epoch.</param>
/// <param name="Symbol">The symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="CellKey">The cell.</param>
/// <param name="TicketGroup">primary or reserve.</param>
/// <param name="ApprovalKind">owner or auto.</param>
/// <param name="QtyAllocated">The allocated quantity.</param>
/// <param name="Qty">The owner's quantity after any size-down.</param>
/// <param name="ArmedAt">When the ticket was armed.</param>
/// <param name="EntryAt">The start of the fill minute.</param>
/// <param name="EntryPrice">The filled entry.</param>
/// <param name="StopPrice">The initial stop.</param>
/// <param name="TargetPrice">The target, or null.</param>
/// <param name="Exit">The exit, or null while the trade is open.</param>
/// <param name="Basis">The label of the record.</param>
/// <param name="CostBasis">What the exit's rupees and R are net of; the gross label while the trade is open.</param>
public sealed record PaperEntryView(
    long Id,
    long PlanId,
    long PlanItemId,
    Guid AccountId,
    long EpochId,
    string Symbol,
    string Side,
    string StrategyId,
    string CellKey,
    string TicketGroup,
    string ApprovalKind,
    int QtyAllocated,
    int Qty,
    DateTimeOffset ArmedAt,
    DateTimeOffset EntryAt,
    decimal EntryPrice,
    decimal StopPrice,
    decimal? TargetPrice,
    PaperExitView? Exit,
    string Basis,
    string CostBasis);

/// <summary>The paper entries with the label and the limits every paper response carries.</summary>
/// <param name="Basis">The label.</param>
/// <param name="Limits">The honest limits.</param>
/// <param name="Items">The entries, newest first.</param>
public sealed record PaperEntryList(string Basis, IReadOnlyList<string> Limits, IReadOnlyList<PaperEntryView> Items);

/// <summary>One session processed by a capture run.</summary>
/// <param name="Id">The row id.</param>
/// <param name="SessionDate">The session.</param>
/// <param name="StartedAt">Start.</param>
/// <param name="FinishedAt">End.</param>
/// <param name="Outcome">done, partial or failed.</param>
/// <param name="Seen">Armed allocations of the session.</param>
/// <param name="EntriesWritten">Entries written.</param>
/// <param name="ExitsWritten">Exits written.</param>
/// <param name="SkipsWritten">Skips written.</param>
/// <param name="Pending">Pairs left for a later run.</param>
/// <param name="Integrity">Stored entries a later replay disagreed with.</param>
/// <param name="Manual">Whether a person started it.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record CaptureRunView(long Id, DateOnly SessionDate, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Outcome, int Seen, int EntriesWritten, int ExitsWritten, int SkipsWritten, int Pending, int Integrity, bool Manual, string Reason);

/// <summary>The capture runs with the label and the limits.</summary>
/// <param name="Basis">The label.</param>
/// <param name="Limits">The honest limits.</param>
/// <param name="Items">The rows, newest first.</param>
public sealed record CaptureRunList(string Basis, IReadOnlyList<string> Limits, IReadOnlyList<CaptureRunView> Items);
