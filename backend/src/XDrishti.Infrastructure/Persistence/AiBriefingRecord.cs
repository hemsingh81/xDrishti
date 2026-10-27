namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.briefings</c> (spec P9-06): one row per (kind, subject, version); insert-only. The facts the text was built from are stored with it.</summary>
internal sealed class AiBriefingRecord
{
    public long Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Source { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public string Text { get; set; } = string.Empty;

    public string DeterministicText { get; set; } = string.Empty;

    public string FactsJson { get; set; } = string.Empty;

    public string FactsHash { get; set; } = string.Empty;

    public string? ModelId { get; set; }

    public string? ModelSha256 { get; set; }

    public string PromptVersion { get; set; } = string.Empty;

    public string ParamsJson { get; set; } = string.Empty;

    public int WordCount { get; set; }

    public string BuiltBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
