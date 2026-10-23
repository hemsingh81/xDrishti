namespace XDrishti.Domain.Planning;

/// <summary>The numbers of doc 10 section 8 over a set of graded tickets (P7-13, ADR 0090).</summary>
/// <param name="Basis">Always <see cref="TicketGrade.Basis"/>.</param>
/// <param name="Plans">Plan-days covered.</param>
/// <param name="Curves">Top-1, top-3, top-5 and top-all by rank over primary and reserve tickets.</param>
/// <param name="Groups">Primary, reserve and rejected.</param>
/// <param name="Decisions">Approved, modified, rejected, lapsed and undecided (primary and reserve tickets only).</param>
/// <param name="Comparisons">Did the reserve, the rejection and the owner's refusals help.</param>
/// <param name="Notes">General notes, the honest limits.</param>
public sealed record GradeReportData(string Basis, int Plans, IReadOnlyList<GradeCurve> Curves, IReadOnlyList<GradeStats> Groups, IReadOnlyList<GradeStats> Decisions, IReadOnlyList<GroupComparison> Comparisons, IReadOnlyList<string> Notes);
