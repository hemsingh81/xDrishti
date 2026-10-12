namespace XDrishti.Domain.Market.Bars;

/// <summary>A bar size. <see cref="M1"/> is the stored 1-minute data; every other timeframe is built from it by the application (ADR 0016 §6).</summary>
public enum Timeframe
{
    M1,
    M5,
    M10,
    M15,
    M30,
    H1,
    D1,
    W1,
    Mo1,
}

/// <summary>The stored and wire spelling of <see cref="Timeframe"/>: <c>1m 5m 10m 15m 30m 1h 1D 1W 1M</c> (case matters: <c>1m</c> is a minute, <c>1M</c> a month).</summary>
public static class Timeframes
{
    /// <summary>The derived timeframes, shortest first.</summary>
    public static readonly IReadOnlyList<Timeframe> Aggregated =
        [Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1];

    /// <summary>The timeframes that can be read: 1m and every derived one.</summary>
    public static readonly IReadOnlyList<Timeframe> All = [Timeframe.M1, .. Aggregated];

    /// <summary>The intraday ones, anchored to the 09:15 session open.</summary>
    public static readonly IReadOnlyList<Timeframe> Intraday = [Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    public static string ToCode(this Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => "1m",
        Timeframe.M5 => "5m",
        Timeframe.M10 => "10m",
        Timeframe.M15 => "15m",
        Timeframe.M30 => "30m",
        Timeframe.H1 => "1h",
        Timeframe.D1 => "1D",
        Timeframe.W1 => "1W",
        Timeframe.Mo1 => "1M",
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe)),
    };

    public static bool TryParse(string? code, out Timeframe timeframe)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.ToCode(), code, StringComparison.Ordinal))
            {
                timeframe = candidate;
                return true;
            }
        }

        timeframe = default;
        return false;
    }

    /// <summary>The length in minutes of an intraday timeframe (1 for 1m); 0 for the calendar ones (day, week, month).</summary>
    public static int Minutes(this Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => 1,
        Timeframe.M5 => 5,
        Timeframe.M10 => 10,
        Timeframe.M15 => 15,
        Timeframe.M30 => 30,
        Timeframe.H1 => 60,
        _ => 0,
    };
}
