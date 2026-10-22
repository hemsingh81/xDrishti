namespace XDrishti.Infrastructure.Research;

/// <summary>
/// A row of <c>research.sim_comparisons</c> (spec P4-05, D10): the header of a stored comparison of exits on the same entries. The members are runs of <c>sim_runs</c> named by key, in order, with their candidate ids and gated counts; the table and
/// the diagnostics are kept as the text of the export. It exists for the model, the migration and the projections of the read path; written only by the adapter's SQL.
/// </summary>
internal sealed class SimComparisonRow
{
    public long Id { get; set; }

    public string ComparisonKey { get; set; } = string.Empty;

    public string EntriesKey { get; set; } = string.Empty;

    public string StrategyId { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public int CommonEntries { get; set; }

    public string[] MemberRunKeys { get; set; } = [];

    public string[] MemberCandidateIds { get; set; } = [];

    public long[] MemberGatedBlocked { get; set; } = [];

    public string ComparisonCsv { get; set; } = string.Empty;

    public string DiagnosticsCsv { get; set; } = string.Empty;

    public string RecordSha256 { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
