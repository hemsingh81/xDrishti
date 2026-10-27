using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Ai;

/// <summary>Reads the state of the local model server (spec P9-01).</summary>
public sealed record GetLlmStatusQuery : IQuery<LlmStatusView>;
