using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>The R distributions of the groups of a group-by, with the evidence of the report.</summary>
/// <param name="BinWidthR">The width of a bin in R.</param>
/// <param name="Dimensions">The dimensions grouped by.</param>
/// <param name="Groups">One distribution per group.</param>
/// <param name="NonAdditive">A trade is in more than one group.</param>
/// <param name="Evidence">The evidence of the whole report.</param>
public sealed record ReportDistribution(double BinWidthR, ValueList<ReportDimension> Dimensions, ValueList<ReportDistributionGroup> Groups, bool NonAdditive, ReportEvidence Evidence);
