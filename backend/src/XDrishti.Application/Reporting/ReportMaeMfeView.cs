namespace XDrishti.Application.Reporting;

/// <summary>The excursions of a report for a lens, one entry per group, as counts in bins (never one point per trade), with the evidence of the report.</summary>
/// <param name="Dimensions">The dimensions grouped by.</param>
/// <param name="NonAdditive">A trade is in more than one group.</param>
/// <param name="Groups">The groups.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record ReportMaeMfeView(IReadOnlyList<string> Dimensions, bool NonAdditive, IReadOnlyList<MaeMfeGroupView> Groups, ReportEvidenceView Evidence);
