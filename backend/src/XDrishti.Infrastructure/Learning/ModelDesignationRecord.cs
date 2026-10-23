namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.model_designations</c> (spec P6-07, ADR 0070): one appended role of a model. Insert-only; the latest row of a model is its role.</summary>
internal sealed class ModelDesignationRecord
{
    public long Id { get; set; }

    public long ModelId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
