using XDrishti.Domain.Assistant.Grounding;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// A stored message. For an assistant message: the finish reason, the grounding status with the spans, the tokens and times, and its tool calls. For a user message: <paramref name="NoAnswerStored"/> is true when the turn has no assistant message (a crash mid-turn).
/// </summary>
public sealed record MessageView(
    long Id, Guid TurnId, int Seq, string Role, string Content, DateTimeOffset CreatedAt, string? FinishReason, string? GroundingStatus, IReadOnlyList<UngroundedSpan> Ungrounded, int? PromptTokens, int? CompletionTokens, int? FirstTokenMs, int? TotalMs, string? PromptHash, bool NoAnswerStored, IReadOnlyList<ToolCallSummary> ToolCalls);
