namespace XDrishti.Infrastructure.Research;

/// <summary>A row of <c>lab.lab_runs</c> (spec P5-02, ADR 0058): one Lab run, the queue entry and the record. It exists for the model and the migration; the store reads and writes it with SQL.</summary>
internal sealed class LabRunRow
{
    public Guid Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string StrategyId { get; set; } = string.Empty;

    public int Version { get; set; }

    public string StrategyHash { get; set; } = string.Empty;

    public string Universe { get; set; } = "{}";

    public DateOnly WindowFrom { get; set; }

    public DateOnly WindowTo { get; set; }

    public string Method { get; set; } = string.Empty;

    public string ConfigHash { get; set; } = string.Empty;

    public string EngineVersion { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string? Stage { get; set; }

    public int ProgressDone { get; set; }

    public int ProgressTotal { get; set; }

    public string[] SimRunKeys { get; set; } = [];

    public string? Counts { get; set; }

    public string Problems { get; set; } = "[]";

    public string Stages { get; set; } = "[]";

    public string Labels { get; set; } = "[]";

    public string? Error { get; set; }

    public string? Host { get; set; }

    public bool CancelRequested { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
