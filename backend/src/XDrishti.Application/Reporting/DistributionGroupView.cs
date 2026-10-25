namespace XDrishti.Application.Reporting;

/// <summary>The R distribution of one group. The bins exist only when <see cref="MeanR"/> is a value or provisional; otherwise it holds the reason there are none.</summary>
/// <param name="Key">The coordinates as text.</param>
/// <param name="Parts">The coordinates.</param>
/// <param name="N">Trades in the group.</param>
/// <param name="MeanR">The mean R, or the reason there is no distribution.</param>
/// <param name="Bins">The bins in order, the open bin below first and the open bin above last.</param>
/// <param name="Evidence">The evidence of the group.</param>
public sealed record DistributionGroupView(string Key, IReadOnlyList<GroupPartView> Parts, int N, MetricView MeanR, IReadOnlyList<DistributionBinView> Bins, GroupEvidenceView Evidence);
