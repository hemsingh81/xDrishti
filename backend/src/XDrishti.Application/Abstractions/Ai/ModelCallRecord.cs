namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A model call outside chat (briefing, draft, proposal, eval): who, which prompt and model, tokens, time and how it ended. No content (the purpose tables hold content).</summary>
public sealed record ModelCallRecord(string Purpose, string Caller, string PromptHash, string ModelId, string? ModelSha256, int? PromptTokens, int? CompletionTokens, int TotalMs, string FinishReason, string Status, string? ErrorCode);
