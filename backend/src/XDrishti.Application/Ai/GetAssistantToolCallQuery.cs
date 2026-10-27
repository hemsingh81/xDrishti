using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>One stored tool call with its envelope.</summary>
public sealed record GetAssistantToolCallQuery(long Id) : IQuery<Result<StoredToolCall>>;
