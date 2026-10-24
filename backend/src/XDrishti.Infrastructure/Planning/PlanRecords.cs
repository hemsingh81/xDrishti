namespace XDrishti.Infrastructure.Planning;

/// <summary>A row of <c>trading.plans</c> (spec P7-03, ADR 0083). Insert-only; never edited.</summary>
internal sealed class PlanRecord
{
    public long Id { get; set; }

    public string PlanKey { get; set; } = string.Empty;

    public DateOnly ForDate { get; set; }

    public DateTimeOffset ReviewCutoff { get; set; }

    public int Version { get; set; }

    public string InputHash { get; set; } = string.Empty;

    public string Basis { get; set; } = string.Empty;

    public bool Provisional { get; set; }

    public string Notice { get; set; } = string.Empty;

    public string Limits { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>trading.plan_items</c>. Insert-only.</summary>
internal sealed class PlanItemRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public int ItemRank { get; set; }

    public string ItemGroup { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public string StrategyId { get; set; } = string.Empty;

    public string StrategyVersionHash { get; set; } = string.Empty;

    public string TriggerText { get; set; } = string.Empty;

    public string StopText { get; set; } = string.Empty;

    public string TargetText { get; set; } = string.Empty;

    public string ValidityText { get; set; } = string.Empty;

    public string[] Reasons { get; set; } = [];

    public string CellKey { get; set; } = string.Empty;

    public int Trades { get; set; }

    public string DataRange { get; set; } = string.Empty;

    public string PWinBasis { get; set; } = string.Empty;

    public double? PWin { get; set; }

    public double? PWinLower { get; set; }

    public double? ConservativeExpectancyR { get; set; }

    public string[] Unavailable { get; set; } = [];

    public string? AdvisoryNote { get; set; }
}

/// <summary>A row of <c>trading.plan_item_allocations</c>. Insert-only.</summary>
internal sealed class PlanItemAllocationRecord
{
    public long Id { get; set; }

    public long ItemId { get; set; }

    public Guid AccountId { get; set; }

    public string Mode { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

/// <summary>A row of <c>trading.plan_events</c>: one state change of a plan (<see cref="ItemId"/> null) or of an item. Insert-only.</summary>
internal sealed class PlanEventRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long? ItemId { get; set; }

    public string FromState { get; set; } = string.Empty;

    public string ToState { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public long? ApprovalId { get; set; }

    public long? PrevEventId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>workflow.approvals</c>: one decision of the owner. Insert-only; the only kind is <c>owner</c>.</summary>
internal sealed class ApprovalRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long? ItemId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? ConfirmReason { get; set; }

    public string? Modifications { get; set; }

    public DateTimeOffset Cutoff { get; set; }

    public DateTimeOffset DecidedAt { get; set; }

    public long? SettingsId { get; set; }

    public string? SettingsHash { get; set; }

    public string? PolicyVersion { get; set; }
}

/// <summary>A row of <c>workflow.autonomy_settings</c>: one change of the autonomy settings by the owner. Insert-only; the newest row is the current settings. The hash and the time are stamped by the database.</summary>
internal sealed class AutonomySettingRecord
{
    public long Id { get; set; }

    public bool AutoApproveTickets { get; set; }

    public bool PromoteFromReserve { get; set; }

    public decimal MinPWin { get; set; }

    public int MaxAutoPerDay { get; set; }

    public string SnapshotHash { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>A row of <c>trading.plan_runs</c>: one attempt of the pipeline. Insert-only.</summary>
internal sealed class PlanRunRecord
{
    public long Id { get; set; }

    public DateOnly ForDate { get; set; }

    public string? PlanKey { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public string? FailedGate { get; set; }

    public string Reason { get; set; } = string.Empty;

    public long? PlanId { get; set; }

    public bool Manual { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }
}

/// <summary>A row of <c>trading.grades</c>: the grade of one ticket (P7-13). Insert-only, one per ticket.</summary>
internal sealed class GradeRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long ItemId { get; set; }

    public DateOnly ForDate { get; set; }

    public string Status { get; set; } = string.Empty;

    public double? R { get; set; }

    public double? MaeR { get; set; }

    public double? MfeR { get; set; }

    public string? ExitReason { get; set; }

    public double? EntryPrice { get; set; }

    public double? ExitPrice { get; set; }

    public string CostBasis { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Basis { get; set; } = string.Empty;

    public string? RunKey { get; set; }

    public DateTimeOffset GradedAt { get; set; }

    public double? StopPrice { get; set; }
}

/// <summary>A row of <c>trading.journal_entries</c>: one approved ticket on one account that the owner's fills were matched to (P7-14). Insert-only.</summary>
internal sealed class JournalEntryRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long ItemId { get; set; }

    public Guid AccountId { get; set; }

    public DateOnly ForDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? UncertainReason { get; set; }
}

/// <summary>A row of <c>trading.journal_fills</c>: one stored trade and what the journal made of it (P7-14). Insert-only; a trade has one row.</summary>
internal sealed class JournalFillRecord
{
    public long Id { get; set; }

    public Guid TransactionId { get; set; }

    public long? EntryId { get; set; }

    public string Role { get; set; } = string.Empty;

    public long Qty { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateOnly ForDate { get; set; }

    public DateTimeOffset JournaledAt { get; set; }
}

/// <summary>A row of <c>trading.fill_grades</c>: the realised-fills grade of one journal entry (P7-14). Insert-only, one per entry.</summary>
internal sealed class FillGradeRecord
{
    public long Id { get; set; }

    public long EntryId { get; set; }

    public string Status { get; set; } = string.Empty;

    public long Qty { get; set; }

    public double EntryPrice { get; set; }

    public double ExitPrice { get; set; }

    public double? PlanEntryPrice { get; set; }

    public double? PlanStopPrice { get; set; }

    public double? PlanR { get; set; }

    public double? SlippagePrice { get; set; }

    public double? SlippageR { get; set; }

    public decimal GrossPnl { get; set; }

    public decimal Charges { get; set; }

    public double? R { get; set; }

    public string Basis { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset GradedAt { get; set; }
}
