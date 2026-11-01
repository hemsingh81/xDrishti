namespace XDrishti.Domain.Options.Quality;

/// <summary>One stored bar of a rolling series as the stitching check sees it: its start, the strike and the spot in paise, and the close in paise of the underlying's 1-minute bar at the bar's end minute (null when there is none).</summary>
public sealed record RollingPoint(DateTimeOffset Ts, int StrikePx, int SpotPx, int? UnderlyingClosePx);
