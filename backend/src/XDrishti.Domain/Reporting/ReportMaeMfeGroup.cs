using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The excursions of one group. <see cref="Grid"/>, <see cref="Mfe"/> and <see cref="ReachedMfe"/> exist only when the group is a value; otherwise <see cref="WinnersMaeP90"/> holds the reason there are none. Trades with no recorded MAE and MFE are left out of the counts
/// (<see cref="ExcursionN"/> says how many are in).
/// </summary>
/// <param name="Key">Who the group is.</param>
/// <param name="N">Trades in the group.</param>
/// <param name="ExcursionN">Trades of the group with a recorded MAE and MFE (the n behind every count here).</param>
/// <param name="WinnersMaeP90">Nearest-rank 90th percentile of the MAE of the winning trades, or the reason it is not a number.</param>
/// <param name="Grid">The MAE by result grid; null for a non-value group.</param>
/// <param name="Mfe">The MFE histogram; null for a non-value group.</param>
/// <param name="ReachedMfe">How many trades reached each target level at some point; empty for a non-value group.</param>
/// <param name="Evidence">The evidence block of the group.</param>
public sealed record ReportMaeMfeGroup(GroupKey Key, int N, int ExcursionN, Metric WinnersMaeP90, MaeResultGrid? Grid, MfeHistogram? Mfe, ValueList<ReachedLevel> ReachedMfe, EvidenceBlock Evidence);
