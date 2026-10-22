using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Lab;

/// <summary>The JSON Schema of the strategy file, as text, for the editor (spec P5-01).</summary>
public sealed record GetStrategySchemaQuery : IQuery<string>;
