namespace XDrishti.Application.Reporting;

/// <summary>A cell with enough sample in its place in the ranking by the credible lower bound of expectancy.</summary>
/// <param name="Rank">1 for the best.</param>
/// <param name="Group">The cell.</param>
/// <param name="LowerBoundPositive">The lower bound of expectancy is above 0: the only reading of "works" the engine allows.</param>
public sealed record RankedCellView(int Rank, GroupView Group, bool LowerBoundPositive);
