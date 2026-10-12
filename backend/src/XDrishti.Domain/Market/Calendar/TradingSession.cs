using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.Calendar;

/// <summary>The session of one trading day in India time: from <see cref="FromMinute"/> up to, not including, <see cref="ToMinute"/> (minutes since midnight).</summary>
public readonly record struct TradingSession(DateOnly Date, int FromMinute, int ToMinute)
{
    /// <summary>The ordinary session, 09:15 to 15:30.</summary>
    public static TradingSession Regular(DateOnly date) => new(date, SessionClock.OpenMinute, SessionClock.CloseMinute);

    /// <summary>How many 1-minute bars a complete session has (375 for the regular one).</summary>
    public int Minutes => ToMinute - FromMinute;

    public bool IsRegular => FromMinute == SessionClock.OpenMinute && ToMinute == SessionClock.CloseMinute;
}
