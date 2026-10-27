namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.prompts</c> (spec P9-03): the text of a prompt, once per hash, so that a stored turn can be replayed. Insert-only.</summary>
internal sealed class AiPromptRecord
{
    public string Hash { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
