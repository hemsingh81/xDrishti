namespace XDrishti.Domain.Assistant;

/// <summary>The verdict of the lexical pre-check: accepted with the statement to run (one trailing semicolon removed), or refused with a single reason word.</summary>
public sealed record SqlCheck(bool Accepted, string Reason, string Statement);
