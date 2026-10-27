namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.briefing_attempts</c> (spec P9-06): a model attempt of a briefing with the text it returned and the guard's findings; insert-only.</summary>
internal sealed class AiBriefingAttemptRecord
{
    public long Id { get; set; }

    public long BriefingId { get; set; }

    public int Attempt { get; set; }

    public long? ModelCallId { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool Passed { get; set; }

    public string FindingsJson { get; set; } = string.Empty;

    public int LatencyMs { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }
}
