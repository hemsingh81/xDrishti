namespace XDrishti.Infrastructure.Research;

/// <summary>A row of <c>lab.registrations</c> (CF-06, ADR 0207). It exists for the model and the migration; the store reads and writes it with SQL.</summary>
internal sealed class RegistrationRecord
{
    public Guid Id { get; set; }

    public string StrategyId { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Rationale { get; set; } = string.Empty;

    public string TextHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string Actor { get; set; } = string.Empty;
}

/// <summary>A row of <c>lab.plan_freezes</c> (CF-06, ADR 0207).</summary>
internal sealed class PlanFreezeRecord
{
    public Guid Id { get; set; }

    public string StrategyId { get; set; } = string.Empty;

    public int Version { get; set; }

    public string PlanHash { get; set; } = string.Empty;

    public Guid FirstRunId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>lab.holdout_blocks</c> (CF-06, ADR 0207).</summary>
internal sealed class HoldOutBlockRecord
{
    public Guid Id { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly HistoryFirst { get; set; }

    public DateOnly HistoryLast { get; set; }

    public int LengthMonths { get; set; }

    public string SettingHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A row of <c>lab.holdout_unlocks</c> (CF-06, ADR 0207).</summary>
internal sealed class HoldOutUnlockRecord
{
    public Guid Id { get; set; }

    public string StrategyId { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset UnlockedAt { get; set; }

    public string Mode { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string SettingHash { get; set; } = string.Empty;

    public bool ReusedWindow { get; set; }

    public DateOnly BlockStart { get; set; }

    public Guid LabRunId { get; set; }

    public string Result { get; set; } = string.Empty;
}
