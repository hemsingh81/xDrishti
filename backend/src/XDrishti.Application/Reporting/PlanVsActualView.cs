using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>Plan versus actual on the wire (P8-04).</summary>
/// <param name="From">Requested first session, or null.</param>
/// <param name="To">Requested last session, or null.</param>
/// <param name="Plans">Plans read (the newest version of each session).</param>
/// <param name="Counts">How the approved ticket lines divide.</param>
/// <param name="Approvals">Approve decisions by kind, from the stored approval rows.</param>
/// <param name="Chain">The steps from plan R to actual R; empty states carry no number.</param>
/// <param name="Missed">Triggered tickets without a fill, outside the chain.</param>
/// <param name="NotDecomposed">Parts never given a number, with the reason.</param>
/// <param name="ByStrategy">Per strategy.</param>
/// <param name="MinTickets">The rule applied.</param>
/// <param name="Evidence">The basis and the limits.</param>
public sealed record PlanVsActualView(
    DateOnly? From,
    DateOnly? To,
    int Plans,
    PlanVsActualCounts Counts,
    IReadOnlyList<ApprovalCountView> Approvals,
    IReadOnlyList<PlanVsActualStepView> Chain,
    PlanVsActualMissedView Missed,
    IReadOnlyList<PlanVsActualNotDecomposed> NotDecomposed,
    IReadOnlyList<PlanVsActualStrategyView> ByStrategy,
    int MinTickets,
    PlanVsActualEvidenceView Evidence);
