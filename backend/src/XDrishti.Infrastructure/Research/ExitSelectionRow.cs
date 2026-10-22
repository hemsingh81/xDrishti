namespace XDrishti.Infrastructure.Research;

/// <summary>
/// A row of <c>research.exit_selections</c> (spec P4-06, D10): the header of a stored exit choice for one cell, made from a stored comparison. The record is kept as the text of <c>exit-selection.json</c>. It exists for the model and the migration;
/// written only by the adapter's SQL.
/// </summary>
internal sealed class ExitSelectionRow
{
    public long Id { get; set; }

    public string SelectionKey { get; set; } = string.Empty;

    public long ComparisonId { get; set; }

    public string StrategyId { get; set; } = string.Empty;

    public string Timeframe { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public string Style { get; set; } = string.Empty;

    public string Regime { get; set; } = string.Empty;

    public string? ChosenCandidateId { get; set; }

    public bool? Stable { get; set; }

    public string RecordJson { get; set; } = string.Empty;

    public string RecordSha256 { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
