namespace XDrishti.Simulation.Replay;

/// <summary>
/// One problem of a strategy text for the Lab (spec P5-01): the model <paramref name="Path"/>, a stable <paramref name="Code"/>, the <paramref name="Message"/>, the <paramref name="Severity"/> (<c>error</c>, <c>warning</c> or <c>information</c>),
/// where the field is in the file (1-based line and column, null when the path is not in the file) and, for a problem of the rules language, the item-relative span (<paramref name="Start"/>, <paramref name="Length"/>: ADR 0036).
/// </summary>
public sealed record StoredIssue(string Path, string Code, string Message, string Severity, int? Line, int? Column, int? EndLine, int? EndColumn, int? Start, int? Length, string? Suggestion);
