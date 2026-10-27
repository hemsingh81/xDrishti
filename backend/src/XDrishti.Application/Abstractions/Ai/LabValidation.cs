namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The answer of <c>lab_validate</c>: whether the text has no error, its hash (the same as <c>POST /api/lab/validate</c>) and every problem. Nothing is stored.</summary>
public sealed record LabValidation(bool Valid, string? Hash, IReadOnlyList<LabValidationProblem> Problems);
