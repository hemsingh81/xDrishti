namespace XDrishti.Api.Endpoints;

/// <summary>The body of <c>POST /api/assistant/chat</c>: the question and, to continue a conversation, its id.</summary>
/// <param name="ConversationId">An existing conversation, or null to start one.</param>
/// <param name="Message">1 to 2,000 characters, no control character.</param>
internal sealed record AssistantChatRequest(Guid? ConversationId, string? Message);
