using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>The distribution of R of one group. <see cref="Bins"/> exist only when <see cref="MeanR"/> carries a number; otherwise <see cref="MeanR"/> holds the reason there are none.</summary>
/// <param name="Key">Who the group is.</param>
/// <param name="N">Trades in the group.</param>
/// <param name="MeanR">The mean R, or the reason there is no distribution.</param>
/// <param name="Bins">The bins in order, the open bin below first and the open bin above last.</param>
/// <param name="Evidence">The evidence block of the group.</param>
public sealed record ReportDistributionGroup(GroupKey Key, int N, Metric MeanR, ValueList<DistributionBin> Bins, EvidenceBlock Evidence);
