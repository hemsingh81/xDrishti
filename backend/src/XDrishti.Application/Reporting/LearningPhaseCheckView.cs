namespace XDrishti.Application.Reporting;

/// <summary>The P6 exit criteria from stored records (P8-04).</summary>
/// <param name="Criteria">The criteria in order.</param>
/// <param name="Limits">The honest limits; never empty.</param>
/// <param name="Text">The printable report.</param>
/// <param name="ElapsedMs">Milliseconds this read took (the check re-reads two runs).</param>
public sealed record LearningPhaseCheckView(IReadOnlyList<LearningPhaseCriterionView> Criteria, IReadOnlyList<string> Limits, string Text, long ElapsedMs);
