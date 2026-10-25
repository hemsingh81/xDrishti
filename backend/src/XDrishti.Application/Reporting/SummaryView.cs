namespace XDrishti.Application.Reporting;

/// <summary>The headline figures of the whole population (the KPIs of doc 13 section 2) with the evidence of the report. A population below the minimum sample has no figure, only the count and the reason.</summary>
/// <param name="All">The whole population as one group.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record SummaryView(GroupView All, ReportEvidenceView Evidence);
