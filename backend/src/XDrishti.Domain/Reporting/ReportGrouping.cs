using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>The groups of a group-by, in a fixed order, with the evidence of the report. <see cref="NonAdditive"/> is true when the basket dimension put a trade into more than one group, so the groups must not be summed.</summary>
/// <param name="Dimensions">The dimensions grouped by, in order.</param>
/// <param name="Groups">The groups, sorted by their coordinates (a not-recorded value last).</param>
/// <param name="NonAdditive">A trade appears in more than one group.</param>
/// <param name="Evidence">The evidence of the whole report.</param>
public sealed record ReportGrouping(ValueList<ReportDimension> Dimensions, ValueList<ReportGroup> Groups, bool NonAdditive, ReportEvidence Evidence);
