namespace XDrishti.Infrastructure.Paper;

/// <summary>A row of <c>paper.epochs</c> (spec P10-01, ADR 0116). Insert-only.</summary>
internal sealed class PaperEpochRecord
{
    public long Id { get; set; }

    public DateOnly StartedOn { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? FingerprintHash { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public long? PrevEpochId { get; set; }
}

/// <summary>A row of <c>paper.epoch_accounts</c>. Insert-only.</summary>
internal sealed class PaperEpochAccountRecord
{
    public long Id { get; set; }

    public long EpochId { get; set; }

    public Guid AccountId { get; set; }

    public decimal Capital { get; set; }

    public string CapitalSource { get; set; } = string.Empty;

    public string Mode { get; set; } = "paper";
}

/// <summary>A row of <c>paper.entries</c>: one paper trade of one account. Insert-only; never changed.</summary>
internal sealed class PaperEntryRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long PlanItemId { get; set; }

    public Guid AccountId { get; set; }

    public long EpochId { get; set; }

    public string MeasureStatus { get; set; } = "measured";

    public string TicketGroup { get; set; } = string.Empty;

    public long ApprovalId { get; set; }

    public string ApprovalKind { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public string StrategyId { get; set; } = string.Empty;

    public string StrategyHash { get; set; } = string.Empty;

    public string CellKey { get; set; } = string.Empty;

    public string TradeKey { get; set; } = string.Empty;

    public int QtyAllocated { get; set; }

    public int Qty { get; set; }

    public DateTimeOffset ArmedAt { get; set; }

    public DateTimeOffset EntryAt { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal EntryExact { get; set; }

    public decimal StopPrice { get; set; }

    public decimal? TargetPrice { get; set; }

    public bool Swing { get; set; }

    public string RunKey { get; set; } = string.Empty;

    public string EngineVersion { get; set; } = string.Empty;

    public string Basis { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>paper.exits</c>: the close of a paper trade, one per entry. Insert-only.</summary>
internal sealed class PaperExitRecord
{
    public long Id { get; set; }

    public long EntryId { get; set; }

    public DateTimeOffset EntryAt { get; set; }

    public DateTimeOffset ExitAt { get; set; }

    public decimal ExitPrice { get; set; }

    public string ExitReason { get; set; } = string.Empty;

    public int HeldSessions { get; set; }

    public decimal GrossPnl { get; set; }

    public decimal? Charges { get; set; }

    public string? ChargeSheetId { get; set; }

    public decimal? NetPnl { get; set; }

    public string CostBasis { get; set; } = string.Empty;

    public double RExact { get; set; }

    public double RGross { get; set; }

    public double? RNet { get; set; }

    public double RSim { get; set; }

    public string RBasis { get; set; } = string.Empty;

    public double MaeR { get; set; }

    public double MfeR { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>paper.skips</c>: a final fact about an armed allocation with no entry. Insert-only.</summary>
internal sealed class PaperSkipRecord
{
    public long Id { get; set; }

    public long PlanId { get; set; }

    public long PlanItemId { get; set; }

    public Guid AccountId { get; set; }

    public long EpochId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>paper.capture_runs</c>: one session processed by one run (coverage). Insert-only.</summary>
internal sealed class PaperCaptureRunRecord
{
    public long Id { get; set; }

    public DateOnly SessionDate { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public int Seen { get; set; }

    public int EntriesWritten { get; set; }

    public int ExitsWritten { get; set; }

    public int SkipsWritten { get; set; }

    public int Pending { get; set; }

    public int Integrity { get; set; }

    public bool Manual { get; set; }

    public string Reason { get; set; } = string.Empty;
}
