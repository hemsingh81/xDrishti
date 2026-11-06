namespace XDrishti.Domain.Readiness;

/// <summary>The whole scorecard of one strategy version (spec CF-11 D1 to D4): the checklist, the score and the two gates. Derived; nothing is stored by building it.</summary>
/// <param name="Rows">The 15 checklist rows.</param>
/// <param name="Score">The readiness score.</param>
/// <param name="GateR">Gate R (Candidate to Paper).</param>
/// <param name="GateS">Gate S (Paper to Active, and later scale-up).</param>
public sealed record ReadinessAssessment(IReadOnlyList<ChecklistRow> Rows, ReadinessScore Score, GateResult GateR, GateResult GateS)
{
    /// <summary>Assesses a strategy version from its inputs.</summary>
    public static ReadinessAssessment Of(ChecklistInputs checklist, ScoreInputs score, HardFailInputs faults, ReadinessOptions options)
    {
        var rows = EdgeChecklist.Evaluate(checklist, options);
        var scored = ReadinessCalculator.Compute(score, faults, options);
        return new ReadinessAssessment(rows, scored, ReadinessGates.GateR(rows), ReadinessGates.GateS(rows, scored.Label));
    }
}
