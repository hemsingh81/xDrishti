namespace XDrishti.Domain.Reporting;

/// <summary>The result of <see cref="PlanVsActual.Build"/> (P8-04).</summary>
/// <param name="Counts">How the lines divide.</param>
/// <param name="Chain">The five steps in order.</param>
/// <param name="Missed">Triggered tickets without a fill.</param>
/// <param name="NotDecomposed">The parts never given a number.</param>
/// <param name="ByStrategy">One row per strategy among the graded tickets, by id.</param>
/// <param name="MinTickets">The rule applied.</param>
public sealed record PlanVsActualResult(
    PlanVsActualCounts Counts,
    IReadOnlyList<PlanVsActualStep> Chain,
    PlanVsActualMissed Missed,
    IReadOnlyList<PlanVsActualNotDecomposed> NotDecomposed,
    IReadOnlyList<PlanVsActualStrategy> ByStrategy,
    int MinTickets);
