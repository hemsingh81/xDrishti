namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The assistant's message, stored once at the end of the turn (the tables are insert-only): the text, the prompt and model, tokens, times, why it ended, and the grounding check (<paramref name="UngroundedJson"/> a JSON array of spans).</summary>
public sealed record AssistantMessageRecord(
    Guid ConversationId, Guid TurnId, string Content, string PromptHash, string ModelId, int? PromptTokens, int? CompletionTokens, int? FirstTokenMs, int TotalMs, string FinishReason, string GroundingStatus, string UngroundedJson);
