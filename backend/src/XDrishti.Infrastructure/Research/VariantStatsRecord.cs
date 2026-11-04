namespace XDrishti.Infrastructure.Research;

/// <summary>A row of <c>lab.variant_stats</c> (CF-03, ADR 0205). It exists for the model and the migration; the store reads and writes it with SQL.</summary>
internal sealed class VariantStatsRecord
{
    public Guid Id { get; set; }

    public string StrategyId { get; set; } = string.Empty;

    public Guid RunId { get; set; }

    public string VariantHash { get; set; } = string.Empty;

    public int N { get; set; }

    public double? MeanR { get; set; }

    public double? SdR { get; set; }

    public double? Skew { get; set; }

    public double? Kurt { get; set; }

    public double? SrPerTrade { get; set; }

    public string Method { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
