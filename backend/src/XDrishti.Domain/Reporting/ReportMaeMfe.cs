using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>The excursion report of the groups of a group-by (spec P8-10): counts in bins, never one point per trade.</summary>
/// <param name="Dimensions">The dimensions grouped by.</param>
/// <param name="Groups">One entry per group.</param>
/// <param name="NonAdditive">A trade is in more than one group.</param>
/// <param name="Evidence">The evidence of the whole report.</param>
public sealed record ReportMaeMfe(ValueList<ReportDimension> Dimensions, ValueList<ReportMaeMfeGroup> Groups, bool NonAdditive, ReportEvidence Evidence);
