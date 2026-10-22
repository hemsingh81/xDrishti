using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Lab;

/// <summary>Validates a strategy text (spec P5-01): read, shape, compile on every declared side and timeframe. Writes nothing.</summary>
public sealed record ValidateStrategyQuery(string Text) : IQuery<StrategyValidation>;
