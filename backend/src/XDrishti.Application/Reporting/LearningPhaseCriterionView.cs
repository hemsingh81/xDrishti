namespace XDrishti.Application.Reporting;

/// <summary>One P6 exit criterion (P8-04).</summary>
/// <param name="Number">1 to 3.</param>
/// <param name="Name">The criterion.</param>
/// <param name="Verdict">MET, NOT MET or NOT EVALUABLE.</param>
/// <param name="Detail">The numbers and the reason.</param>
/// <param name="Caveat">What a MET rests on without being verified here; null when none.</param>
public sealed record LearningPhaseCriterionView(int Number, string Name, string Verdict, string Detail, string? Caveat);
