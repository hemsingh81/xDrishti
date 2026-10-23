namespace XDrishti.Domain.Planning;

/// <summary>Two sets compared by mean R per triggered ticket (P7-13): what the second would have added or avoided.</summary>
/// <param name="Question">The question in words.</param>
/// <param name="Left">The first set.</param>
/// <param name="Right">The second set.</param>
/// <param name="MeanDifference">Mean R of the first minus mean R of the second; null when either has no triggered ticket.</param>
/// <param name="Verdict">first_better, second_better, equal or not_enough_data. Never a claim of significance.</param>
/// <param name="Note">The sample-size note.</param>
public sealed record GroupComparison(string Question, string Left, string Right, double? MeanDifference, string Verdict, string Note);
