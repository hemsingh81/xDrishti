namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.trade_features</c> (spec P6-04, ADR 0066). It exists for the model and the migration; the store reads and writes it with SQL.</summary>
internal sealed class TradeFeatureRecord
{
    public long Id { get; set; }

    public string RunKey { get; set; } = string.Empty;

    public string TradeKey { get; set; } = string.Empty;

    public string FeatureVersion { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;

    public string Timeframe { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public DateOnly TradeDate { get; set; }

    public long DecisionEpochSeconds { get; set; }

    public double[] FeatureValues { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
