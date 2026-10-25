namespace XDrishti.Application.Reporting;

/// <summary>The equity curve of one group. The points exist only when <see cref="TotalR"/> is a value or provisional; a group below the minimum sample has no curve, so no flat line can be drawn for it.</summary>
/// <param name="Key">The coordinates as text.</param>
/// <param name="Parts">The coordinates.</param>
/// <param name="N">Trades in the group.</param>
/// <param name="TotalR">The final running R, or the reason there is no curve.</param>
/// <param name="Points">One point per exit date.</param>
/// <param name="Evidence">The evidence of the group.</param>
public sealed record EquitySeriesView(string Key, IReadOnlyList<GroupPartView> Parts, int N, MetricView TotalR, IReadOnlyList<EquityPointView> Points, GroupEvidenceView Evidence);
