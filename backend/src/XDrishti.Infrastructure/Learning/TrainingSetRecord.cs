namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.training_sets</c> (spec P6-04, ADR 0066). It exists for the model and the migration; the store writes it with SQL.</summary>
internal sealed class TrainingSetRecord
{
    public long Id { get; set; }

    public string SetId { get; set; } = string.Empty;

    public string LabelSetId { get; set; } = string.Empty;

    public string RuleVersion { get; set; } = string.Empty;

    public string FeatureVersion { get; set; } = string.Empty;

    public string Basis { get; set; } = string.Empty;

    public string Sources { get; set; } = string.Empty;

    public string RequiredFeatures { get; set; } = string.Empty;

    public double HalfLifeDays { get; set; }

    public double PriorStrength { get; set; }

    public int MinTrades { get; set; }

    public double CredibleLevel { get; set; }

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public int Labelled { get; set; }

    public int RowCount { get; set; }

    public int Positives { get; set; }

    public int Negatives { get; set; }

    public int Unresolved { get; set; }

    public int NoTarget { get; set; }

    public int DroppedNoFeatures { get; set; }

    public int DroppedUnknown { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
