using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// The grade of one plan text (spec P9-09 AC 5): <paramref name="RawPass"/> the guard accepted the first attempt, <paramref name="RepairedPass"/> it accepted one of the (at most two) attempts, <paramref name="Escape"/> a text it accepted holds a figure the independent audit
/// cannot find in the facts, <paramref name="Covered"/> every primary and reserve symbol is in the text that counts (the accepted one, else the last attempt). <paramref name="Findings"/> are the codes of the first attempt, <paramref name="Absent"/> the escaped figures.
/// </summary>
public sealed record PlanGrade(string CaseId, bool RawPass, bool RepairedPass, bool Escape, bool Covered, ValueList<string> Findings, ValueList<string> Absent)
{
    /// <summary>Passed for pass^k: the repaired text was accepted, did not escape and covers every ticket.</summary>
    public bool Passed => RepairedPass && !Escape && Covered;
}
