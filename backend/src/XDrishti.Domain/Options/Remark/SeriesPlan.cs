namespace XDrishti.Domain.Options.Remark;

/// <summary>The rolling series a trade is re-marked on and its expiry: the kind, the code, the expiry date with its source, the bar width in minutes and the last trading day before the expiry (the D6 exit session).</summary>
public sealed record SeriesPlan(char Flag, int Code, ExpiryDate Expiry, int IntervalMinutes, DateOnly LastSession);
