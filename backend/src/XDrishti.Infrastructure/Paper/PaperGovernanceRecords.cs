namespace XDrishti.Infrastructure.Paper;

/// <summary>A row of <c>paper.config_snapshots</c> (spec P10-05, ADR 0119). Insert-only.</summary>
internal sealed class ConfigSnapshotRecord
{
    public long Id { get; set; }

    public DateTimeOffset TakenAt { get; set; }

    public DateOnly SessionDate { get; set; }

    public string Hash { get; set; } = string.Empty;

    public string Components { get; set; } = "{}";

    public bool Partial { get; set; }

    public string Process { get; set; } = string.Empty;
}

/// <summary>A row of <c>paper.config_changes</c>. Insert-only.</summary>
internal sealed class ConfigChangeRecord
{
    public long Id { get; set; }

    public long FromSnapshotId { get; set; }

    public long ToSnapshotId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Class { get; set; } = string.Empty;

    public string? Before { get; set; }

    public string? After { get; set; }

    public string Effect { get; set; } = string.Empty;

    public long EpochId { get; set; }

    public string? ExplainedBy { get; set; }
}

/// <summary>A row of <c>paper.epoch_notes</c>. Insert-only.</summary>
internal sealed class EpochNoteRecord
{
    public long Id { get; set; }

    public long EpochId { get; set; }

    public long? ChangeId { get; set; }

    public long? HypothesisId { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
