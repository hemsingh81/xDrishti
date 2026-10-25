namespace XDrishti.Application.Reporting;

/// <summary>The evidence block of plan versus actual (P8-04).</summary>
/// <param name="Basis">Text: realised fills beside simulated plan prices.</param>
/// <param name="N">Tickets graded on fills.</param>
/// <param name="FirstDate">Earliest planned session read.</param>
/// <param name="LastDate">Latest planned session read.</param>
/// <param name="Provisional">A plan read is marked provisional.</param>
/// <param name="Limits">The limits that always apply.</param>
public sealed record PlanVsActualEvidenceView(string Basis, int N, DateOnly? FirstDate, DateOnly? LastDate, bool Provisional, IReadOnlyList<string> Limits);
