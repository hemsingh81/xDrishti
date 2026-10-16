namespace XDrishti.Strategies;

/// <summary>
/// One thing wrong (or worth knowing) about a condition, as data: a stable <paramref name="Code"/> (<c>strategy.syntax.*</c>, <c>strategy.lint.*</c>) a program can switch on, a <paramref name="Message"/> a person can read
/// that names the offending text, where it is (<paramref name="Span"/>, with line and column), an optional <paramref name="Suggestion"/> (the replacement text, for example the closest known name) and the
/// <paramref name="Severity"/>. The compiler never throws for a bad condition; it returns every problem it found, in source order.
/// </summary>
public sealed record Problem(string Code, string Message, SourceSpan Span, string? Suggestion = null, ProblemSeverity Severity = ProblemSeverity.Error);
