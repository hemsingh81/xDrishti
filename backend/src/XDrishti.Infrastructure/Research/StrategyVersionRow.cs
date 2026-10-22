namespace XDrishti.Infrastructure.Research;

/// <summary>A row of <c>lab.strategy_versions</c> (spec P3-02d, D2): one immutable version of a strategy, with its text, canonical text and hash. It exists for the model and the migration; written only by the store's SQL.</summary>
internal sealed class StrategyVersionRow
{
    public string StrategyId { get; set; } = string.Empty;

    public int Version { get; set; }

    public short SchemaVersion { get; set; }

    public string Hash { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    public string CanonicalJson { get; set; } = string.Empty;

    public string? ParentHash { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
