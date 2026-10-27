namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.eval_baselines</c> (spec P9-09 AC 11): the owner accepted a run. At most one row per run; the newest row is the current baseline. Insert-only.</summary>
internal sealed class AiEvalBaselineRecord
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public string AcceptedBy { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public DateTimeOffset AcceptedAt { get; set; }
}
