namespace XDrishti.Application.Reporting;

/// <summary>
/// Baskets side by side (BK-07): the relative strength return of each over 21, 63, 126 and 252 sessions, its rank (1 = strongest) on <c>RankWindow</c> and the return over the annualised volatility. A basket with no value has no rank, only its state. No label "best":
/// when a pair of neighbours is within its noise, <c>Separated</c> is false and the screen shows no order.
/// </summary>
/// <param name="Window">The window and where it came from.</param>
/// <param name="Index">The index the strengths are against.</param>
/// <param name="RankWindow">Sessions of the relative strength return the ranks are on; null when fewer than two baskets have one.</param>
/// <param name="Rows">One per basket, in the order asked.</param>
/// <param name="Overlap">The Jaccard index of the member sets of each pair of baskets at the end of the window.</param>
/// <param name="Separated">False means "not separated": every gap is not larger than its standard error (indicative, pairwise, uncorrected; not a significance test).</param>
/// <param name="Evidence">The evidence block.</param>
public sealed record BasketRankingView(
    BenchmarkWindowView Window,
    ProfileIndexView Index,
    int? RankWindow,
    IReadOnlyList<RankingRowView> Rows,
    IReadOnlyList<OverlapView> Overlap,
    bool Separated,
    BenchmarkEvidenceView Evidence);

/// <summary>One basket on the ranking board.</summary>
public sealed record RankingRowView(
    Guid BasketId,
    string Name,
    MetricView RsReturn21,
    MetricView RsReturn63,
    MetricView RsReturn126,
    MetricView RsReturn252,
    MetricView RsRank,
    MetricView RiskAdjusted,
    string State);

/// <summary>How much two baskets share: the Jaccard index of their member sets.</summary>
public sealed record OverlapView(Guid A, Guid B, MetricView Jaccard);
