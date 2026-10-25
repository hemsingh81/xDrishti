using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The equity curve of one group. <see cref="Points"/> exist only when <see cref="TotalR"/> carries a number (value or provisional); a group below the minimum sample or with an unrecorded coordinate has no points, so no consumer can draw a flat line for it.
/// </summary>
/// <param name="Key">Who the group is.</param>
/// <param name="N">Trades in the group.</param>
/// <param name="TotalR">The final running R, or the reason there is no curve.</param>
/// <param name="Points">One point per exit date in order.</param>
/// <param name="Evidence">The evidence block of the group.</param>
public sealed record ReportEquitySeries(GroupKey Key, int N, Metric TotalR, ValueList<EquityPoint> Points, EvidenceBlock Evidence);
