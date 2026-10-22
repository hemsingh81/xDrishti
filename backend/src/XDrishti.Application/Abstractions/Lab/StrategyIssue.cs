namespace XDrishti.Application.Abstractions.Lab;

/// <summary>
/// One located problem of a strategy text (spec P5-01): the model path, a stable code, a message, the severity (<c>error</c>, <c>warning</c>, <c>information</c>), the place of the field in the file (1-based; null when the path is not in the
/// file) and, for a problem of the rules language, the item-relative span (zero-based offset and length inside the condition text, ADR 0036).
/// </summary>
public sealed record StrategyIssue(string Path, string Code, string Message, string Severity, int? Line, int? Column, int? EndLine, int? EndColumn, int? Start, int? Length, string? Suggestion);
