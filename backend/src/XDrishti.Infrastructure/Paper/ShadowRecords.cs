namespace XDrishti.Infrastructure.Paper;

/// <summary>A row of <c>paper.shadow_entries</c> (spec CF-08, ADR 0211): one candidate of a nightly plan, written before its session. Insert-only; the database stamps <c>written_at</c>.</summary>
internal sealed class ShadowEntryRecord
{
    public long Id { get; set; }

    public DateOnly SessionDate { get; set; }

    public long PlanId { get; set; }

    public long PlanItemId { get; set; }

    public string TradeKey { get; set; } = string.Empty;

    public string TicketGroup { get; set; } = string.Empty;

    public string CellKey { get; set; } = string.Empty;

    public string StrategyHash { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public decimal? EntryLimit { get; set; }

    public decimal? Stop { get; set; }

    public decimal? Target { get; set; }

    public string TimeRule { get; set; } = string.Empty;

    public string? ConfigHash { get; set; }

    public string? ModelId { get; set; }

    public double? PWin { get; set; }

    public string? FeatureVersion { get; set; }

    public string? Features { get; set; }

    public bool CountsForEdge { get; set; }

    public long? EdgeEpoch { get; set; }

    public DateTimeOffset WrittenAt { get; set; }

    public string RowHash { get; set; } = string.Empty;
}

/// <summary>A row of <c>paper.shadow_sessions</c>: one session of the chain, ok or a gap. Insert-only.</summary>
internal sealed class ShadowSessionRecord
{
    public DateOnly SessionDate { get; set; }

    public int NRows { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Digest { get; set; } = string.Empty;

    public string? PrevDigest { get; set; }

    public DateTimeOffset WrittenAt { get; set; }
}

/// <summary>A row of <c>paper.shadow_grades</c>: the grade of one shadow entry, written once by the grader (a later slice). Insert-only; the database stamps <c>graded_at</c>.</summary>
internal sealed class ShadowGradeRecord
{
    public long Id { get; set; }

    public long EntryId { get; set; }

    public string Status { get; set; } = string.Empty;

    public double? RSim { get; set; }

    public string? RBasis { get; set; }

    public double? RGross { get; set; }

    public DateTimeOffset? EntryAt { get; set; }

    public DateTimeOffset? ExitAt { get; set; }

    public string? ExitReason { get; set; }

    public string EngineVersion { get; set; } = string.Empty;

    public string? SheetId { get; set; }

    public DateTimeOffset GradedAt { get; set; }
}

/// <summary>A row of <c>paper.shadow_settings</c>: one change of the switch <c>paper.shadow.enabled</c>. Insert-only; the newest row is current and no row means OFF.</summary>
internal sealed class ShadowSettingRecord
{
    public long Id { get; set; }

    public bool Enabled { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>A row of <c>paper.evidence_sets</c>: the frozen evidence set of an edge epoch (spec AM-01 GAP-09). Insert-only; the newest row of an epoch is current. The store reads and writes it with SQL; the class exists for the model and the migration.</summary>
internal sealed class EvidenceSetRecord
{
    public long Id { get; set; }

    public long EdgeEpoch { get; set; }

    public string Versions { get; set; } = "[]";

    public string SetHash { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FirstWriteAt { get; set; }
}
