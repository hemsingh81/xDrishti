namespace XDrishti.Domain.Readiness;

/// <summary>The verdict of one gate (spec CF-11 D1, D6): whether it passes and what stops it. Derived and read only.</summary>
/// <param name="Name"><c>R</c> (research to paper-following) or <c>S</c> (paper to scale-up).</param>
/// <param name="Passed">True only when every required row passes (and, for S, the label is "Ready for the next phase").</param>
/// <param name="Failing">The required rows that are not a pass, with their status (an unavailable row is among them).</param>
/// <param name="LabelOk">For gate S, whether the label is "Ready for the next phase"; true for gate R (the label is not part of it).</param>
public sealed record GateResult(string Name, bool Passed, IReadOnlyList<ChecklistRow> Failing, bool LabelOk);
