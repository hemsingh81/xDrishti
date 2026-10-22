namespace XDrishti.Infrastructure.Research;

/// <summary>A row of <c>lab.lab_results</c> (spec P5-04, ADR 0060). It exists for the model and the migration; the store reads and writes it with SQL.</summary>
internal sealed class LabResultRow
{
    public Guid Id { get; set; }

    public Guid LabRunId { get; set; }

    public string StrategyId { get; set; } = string.Empty;

    public string StrategyHash { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public int VariantCount { get; set; }

    public double AdjustmentFactor { get; set; }

    public string Result { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
