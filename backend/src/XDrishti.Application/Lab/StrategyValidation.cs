using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Application.Lab;

/// <summary>The answer of validate (spec P5-01): whether the text has no error, its hash when it has none, what it declares and every problem. Invalid text is still an answer, not a failure.</summary>
public sealed record StrategyValidation(
    bool Valid,
    string? Hash,
    string? Id,
    string? Style,
    int? Version,
    IReadOnlyList<string> Timeframes,
    IReadOnlyList<string> Sides,
    IReadOnlyList<StrategyIssue> Problems);
