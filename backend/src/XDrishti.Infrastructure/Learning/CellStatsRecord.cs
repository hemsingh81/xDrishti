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
}
