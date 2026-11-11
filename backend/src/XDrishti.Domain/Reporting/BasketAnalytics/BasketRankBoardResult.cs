namespace XDrishti.Domain.Reporting;

/// <summary>The ranking of baskets (BK-07 m8 and Decision 3). <paramref name="RankWindow"/> is the relative strength window the ranks rest on; <paramref name="Separated"/> is true only when every neighbouring pair is further apart than its noise.</summary>
/// <param name="RankWindow">Sessions of the RS return that was ranked; null when fewer than two baskets have any.</param>
/// <param name="Rows">One per basket, in the order given.</param>
/// <param name="Separated">False means "not separated": the order is not to be shown.</param>
public sealed record BasketRankBoardResult(int? RankWindow, IReadOnlyList<RankedBasket> Rows, bool Separated);
