namespace XDrishti.Domain.Options.Iv;

/// <summary>The rolling series the <c>iv-1</c> features of one trade date read (P14-11): the kind (<c>W</c> or <c>M</c>), the front code and its expiry date, the back code for the term slope (null when the archive holds no second series), and the bar width in minutes.</summary>
public sealed record IvSeriesPlan(char Flag, int FrontCode, DateOnly FrontExpiry, int? BackCode, int IntervalMinutes);
