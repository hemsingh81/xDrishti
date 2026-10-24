namespace XDrishti.Domain.Reporting;

/// <summary>A cell that was looked at and is not in the ranking, with the reason; listed after the ranked ones, never hidden.</summary>
/// <param name="Group">The cell (its figures are non-values, only its count is a number).</param>
/// <param name="Reason">Why it is not ranked.</param>
public sealed record UnrankedCell(ReportGroup Group, string Reason);
