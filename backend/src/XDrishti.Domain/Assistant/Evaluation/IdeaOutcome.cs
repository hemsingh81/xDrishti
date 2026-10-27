using XDrishti.Domain.Assistant.Drafting;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>What the draft pipeline made of an idea: the status (<c>valid</c>, <c>invalid</c>, <c>declined</c> or <c>failed</c>), the text and its plain tree, and the model's answer.</summary>
public sealed record IdeaOutcome(string Status, string? Yaml, string? TreeJson, DraftAnswer? Answer);
