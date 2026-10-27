namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.eval_runs</c> (spec P9-09 Contract): one row per eval run, written once when the run ends; insert-only. The fingerprint and the hashes say exactly what was measured.</summary>
internal sealed class AiEvalRunRecord
{
    public Guid Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public string Target { get; set; } = string.Empty;

    public string Profile { get; set; } = string.Empty;

    public string[] Sets { get; set; } = [];

    public int Repeats { get; set; }

    public bool Gating { get; set; }

    public string ModelId { get; set; } = string.Empty;

    public string ModelSha256 { get; set; } = string.Empty;

    public string RuntimeDigest { get; set; } = string.Empty;

    public string ParamsJson { get; set; } = string.Empty;

    public string PromptVersionsJson { get; set; } = string.Empty;

    public string SetsHash { get; set; } = string.Empty;

    public string ThresholdsHash { get; set; } = string.Empty;

    public string FixtureHash { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Verdict { get; set; } = string.Empty;

    public string SummaryJson { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;
}
