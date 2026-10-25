namespace XDrishti.Application.Reporting;

/// <summary>The eight auto-insights, always in the same order, with the evidence of the report.</summary>
/// <param name="Insights">The insights.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record InsightsView(IReadOnlyList<InsightView> Insights, ReportEvidenceView Evidence);
