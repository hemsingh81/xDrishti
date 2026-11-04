namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.cell_stats</c> (spec P6-01, ADR 0064). It exists for the model and the migration; the store reads and writes it with SQL.</summary>
internal sealed class CellStatsRecord
{
    public long Id { get; set; }

    public string SnapshotKey { get; set; } = string.Empty;

    public DateOnly AsOf { get; set; }

    public string CellKey { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;

    public string Timeframe { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public string Style { get; set; } = string.Empty;

    public string ExitPolicy { get; set; } = string.Empty;

    public string Regime { get; set; } = string.Empty;

    public string Window { get; set; } = string.Empty;

    public bool Costed { get; set; }

    public int N { get; set; }

    public int Wins { get; set; }

    public double EffectiveN { get; set; }

    public double? WinRateRaw { get; set; }

    public double PriorRate { get; set; }

    public double WinRatePosterior { get; set; }

    public double WinRateLower { get; set; }

    public double WinRateUpper { get; set; }

    public double? AvgWinR { get; set; }

    public double? AvgLossR { get; set; }

    public double? ExpectancyR { get; set; }

    public double? ProfitFactor { get; set; }

    public double MaxDrawdownR { get; set; }

    public double? MaeP50 { get; set; }

    public double? MaeP90 { get; set; }

    public double? MfeP50 { get; set; }

    public double? MfeP90 { get; set; }

    public bool Enough { get; set; }

    public double HalfLifeDays { get; set; }

    public double PriorStrength { get; set; }

    public int MinTrades { get; set; }

    public double CredibleLevel { get; set; }

    public string Sources { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    // CF-01 (ADR 0203): effective n, power and minimum track record of the window's trades; all null in a row written before the migration ("not computed")
    public double? NEff { get; set; }

    public double? DesignEffect { get; set; }

    public string? NEffMethod { get; set; }

    public int? NeededTrades { get; set; }

    public double? MinDetectableEdgeR { get; set; }

    public int? MinTrl { get; set; }

    public bool? EnoughByNEff { get; set; }

    public double? SpanYears { get; set; }

    public string? NEffMode { get; set; }

    public string? NEffNote { get; set; }

    public double? TargetEdgeR { get; set; }

    // CF-02 (ADR 0204): the shrunk figures of the row; all null in a row written before the migration ("not computed")
    public string? ParentLevel { get; set; }

    public double? ParentMeanR { get; set; }

    public double? TauSquared { get; set; }

    public double? Blend { get; set; }

    public double? ShrunkMeanR { get; set; }

    public double? ShrunkLowerR { get; set; }

    public double? EstimatedStrength { get; set; }

    public double? ShrunkWinRate { get; set; }

    public string? TauSource { get; set; }

    public string? StrengthSource { get; set; }

    public double? ShrinkN { get; set; }

    public string? ShrinkMode { get; set; }

    public string? ShrinkNote { get; set; }
}
