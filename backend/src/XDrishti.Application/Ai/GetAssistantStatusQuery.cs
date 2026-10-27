using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Ai;

/// <summary>Reads the assistant's settings and the state of everything it depends on. Reads only.</summary>
public sealed record GetAssistantStatusQuery : IQuery<AssistantStatusView>;
