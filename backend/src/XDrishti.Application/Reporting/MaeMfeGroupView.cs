namespace XDrishti.Application.Reporting;

/// <summary>The excursions of one group. The grid, the histogram and the levels exist only for a group that is a value; otherwise <c>WinnersMaeP90</c> holds the reason.</summary>
/// <param name="Key">The group key text.</param>
/// <param name="Parts">The coordinates.</param>
/// <param name="N">Trades in the group.</param>
/// <param name="ExcursionN">Trades with a recorded MAE and MFE: the n behind every count.</param>
/// <param name="WinnersMaeP90">Nearest-rank 90th percentile of the MAE of winners, or its state.</param>
/// <param name="Grid">MAE by result counts; null for a non-value group.</param>
/// <param name="Mfe">The MFE histogram; null for a non-value group.</param>
/// <param name="ReachedMfe">Trades that reached 1, 2 and 3 R at some point; empty for a non-value group.</param>
/// <param name="Evidence">The evidence of the group.</param>
public sealed record MaeMfeGroupView(string Key, IReadOnlyList<GroupPartView> Parts, int N, int ExcursionN, MetricView WinnersMaeP90, MaeGridView? Grid, MfeHistogramView? Mfe, IReadOnlyList<ReachedMfeView> ReachedMfe, GroupEvidenceView Evidence);
