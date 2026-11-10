namespace XDrishti.Domain.Baskets.Derived;

/// <summary>One 1-minute bar. <c>Time</c> is the start of the minute; intraday arrays are sorted by time ascending.</summary>
public readonly record struct IntradayBar(DateTimeOffset Time, double Open, double High, double Low, double Close, double Volume);
