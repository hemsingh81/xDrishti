namespace XDrishti.Application.Reporting;

/// <summary>The R distributions of a report for a lens, one per group, with the evidence of the report.</summary>
/// <param name="BinWidthR">The width of a bin in R.</param>
/// <param name="Dimensions">The dimensions grouped by.</param>
/// <param name="NonAdditive">A trade is in more than one group.</param>
/// <param name="Groups">The distributions.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record DistributionView(double BinWidthR, IReadOnlyList<string> Dimensions, bool NonAdditive, IReadOnlyList<DistributionGroupView> Groups, ReportEvidenceView Evidence);
