using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Market.Strength;

/// <summary>One sector (<c>Excluded</c>: members left out for an ex-date in the window, counted) over one window: the equal-weight member return (fraction), the members that had the full window and the rank among sectors with a value (1 is the strongest, ties share the lower rank).</summary>
public sealed record SectorReturn(string Sector, int Window, Metric Return, int Members, int? Rank, int Excluded = 0);
