using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>The equity curves of the groups of a group-by, in the order of the keys, with the evidence of the report. <see cref="NonAdditive"/> is true when the basket dimension put a trade into more than one curve.</summary>
/// <param name="Dimensions">The dimensions grouped by.</param>
/// <param name="Series">One curve per group.</param>
/// <param name="NonAdditive">A trade is in more than one curve.</param>
/// <param name="Evidence">The evidence of the whole report.</param>
public sealed record ReportEquity(ValueList<ReportDimension> Dimensions, ValueList<ReportEquitySeries> Series, bool NonAdditive, ReportEvidence Evidence);
