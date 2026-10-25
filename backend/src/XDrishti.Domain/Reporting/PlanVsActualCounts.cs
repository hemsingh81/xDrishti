namespace XDrishti.Domain.Reporting;

/// <summary>How the approved ticket lines divide (P8-04). A graded line with a zero or missing planned risk per share is counted under <c>NoPlanLevels</c>.</summary>
/// <param name="ApprovedLines">Approved ticket lines.</param>
/// <param name="WithFills">Lines with a matched fill.</param>
/// <param name="GradedOnFills">Lines in the chain.</param>
/// <param name="Uncertain">Fills that may not be the ticket's alone: no R.</param>
/// <param name="NoPlanLevels">Fills without usable plan levels.</param>
/// <param name="NotFinished">Position still open or grade not there yet.</param>
/// <param name="WithoutFills">Lines with no matched fill.</param>
public sealed record PlanVsActualCounts(int ApprovedLines, int WithFills, int GradedOnFills, int Uncertain, int NoPlanLevels, int NotFinished, int WithoutFills);
