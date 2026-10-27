namespace XDrishti.Application.Abstractions.Ai;

/// <summary>A located problem of a strategy text.</summary>
public sealed record LabValidationProblem(int? Line, int? Column, string Code, string Message, string Severity);
