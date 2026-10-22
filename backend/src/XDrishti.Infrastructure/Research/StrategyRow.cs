namespace XDrishti.Infrastructure.Research;

/// <summary>A row of <c>lab.strategies</c> (spec P3-02d, D1): one strategy family, written once. It exists for the model and the migration; written only by the store's SQL.</summary>
internal sealed class StrategyRow
{
    public string Id { get; set; } = string.Empty;

    public string Style { get; set; } = string.Empty;

    public bool BuiltIn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
