using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The grade of one idea (spec P9-09 AC 6): <paramref name="Accepted"/> the outcome is the right kind (a valid draft, or a decline where one is right), <paramref name="PassedAssertions"/> over <paramref name="TotalAssertions"/> is the intent,
/// <paramref name="DeclineCorrect"/> is set for the decline kinds only.
/// </summary>
public sealed record IdeaGrade(string CaseId, bool Accepted, int PassedAssertions, int TotalAssertions, bool? DeclineCorrect, ValueList<string> Failed)
{
    public double Intent => TotalAssertions == 0 ? 1.0 : (double)PassedAssertions / TotalAssertions;
}
