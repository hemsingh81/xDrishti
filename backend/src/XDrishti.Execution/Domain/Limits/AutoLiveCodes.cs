namespace XDrishti.Execution.Domain.Limits;

/// <summary>
/// The refusal codes of the limits and the <c>auto_live</c> guard (spec P12-09 criteria 1 to 7 and 12). Each names one rule; an action-log row carries them, a log line carries them, never a price or a symbol. The database trigger
/// <c>execution.check_ledger_entry</c> uses the same codes for the rules it proves.
/// </summary>
public static class AutoLiveCodes
{
    public const string NotAuto = "mode.not_auto";
    public const string LimitsIncomplete = "limits.incomplete";
    public const string NotionalOrder = "cap.notional_order";
    public const string NotionalDay = "cap.notional_day";
    public const string OrdersDay = "cap.orders_day";
    public const string OpenPositions = "cap.open_positions";
    public const string LossDay = "cap.loss_day";
    public const string LossUnavailable = "cap.loss_unavailable";
    public const string LedgerUnavailable = "cap.unavailable";
    public const string WindowClosed = "window.closed";
    public const string InstrumentNotAllowed = "instrument.not_allowed";
    public const string ProductNotAllowed = "product.not_allowed";
    public const string ApprovalNotOwner = "approval.not_owner";
    public const string PlanProvisional = "plan.provisional";
    public const string QtyAbovePlan = "qty.above_plan";
    public const string ExposureDuplicate = "exposure.duplicate";
    public const string StrategyFirstUse = "strategy.first_use";
    public const string PretradeFailed = "pretrade.failed";
    public const string NotUnlocked = "unlock.missing";
    public const string NotAttended = "owner.not_attended";
    public const string NotReady = "readiness.not_ready";
    public const string FeedStale = "feed.stale";
    public const string ReconBreak = "recon.break";
    public const string Halted = "halt.active";
}
