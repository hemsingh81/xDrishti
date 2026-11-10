namespace XDrishti.Domain.Reporting;

/// <summary>Breadth inside a basket on one day (BK-06 m9).</summary>
/// <param name="AboveSma20">Share of voters with a close above their 20-day average.</param>
/// <param name="AboveSma50">The same for 50 days.</param>
/// <param name="AboveSma200">The same for 200 days.</param>
/// <param name="AdvanceShare">Advancers over advancers plus decliners.</param>
/// <param name="NewHighShare">Share of voters with 252 sessions at their 252-session high.</param>
/// <param name="NoBar">Members with no bar on the day, left out of everything.</param>
public sealed record BasketBreadthResult(BreadthFigure AboveSma20, BreadthFigure AboveSma50, BreadthFigure AboveSma200, BreadthFigure AdvanceShare, BreadthFigure NewHighShare, IReadOnlyList<string> NoBar);
