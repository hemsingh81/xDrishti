namespace XDrishti.Application.Learning;

/// <summary>The newest evaluations of a family, newest first, at most 100.</summary>
public sealed record ExtensionEvaluationList(string Family, IReadOnlyList<ExtensionEvaluationView> Items);
