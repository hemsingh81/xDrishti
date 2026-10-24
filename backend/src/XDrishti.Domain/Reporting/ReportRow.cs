namespace XDrishti.Domain.Reporting;

/// <summary>A trade of the population with the labels of its run and the R of the report's basis, resolved once so the grouping reads one thing.</summary>
internal sealed record ReportRow(ReportTrade Trade, string? Style, string? Exit, double R, bool Provisional);
