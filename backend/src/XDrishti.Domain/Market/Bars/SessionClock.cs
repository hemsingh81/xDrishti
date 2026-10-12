namespace XDrishti.Domain.Market.Bars;

/// <summary>The NSE cash session in India time (a fixed UTC+05:30 offset, <see cref="IndiaTime"/>): 09:15 to 15:30, 375 one-minute bars.</summary>
public static class SessionClock
{
    public const int OpenMinute = 9 * 60 + 15;

    /// <summary>The first minute after the session (15:30).</summary>
    public const int CloseMinute = 15 * 60 + 30;

    public const int RegularBars = CloseMinute - OpenMinute;

    /// <summary>Special evening sessions (Muhurat) are accepted from this India time on…</summary>
    public const int EveningSessionFromMinute = 17 * 60 + 30;

    /// <summary>…until this one (20:00).</summary>
    public const int EveningSessionToMinute = 20 * 60;

    private const long SecondsPerDay = 86_400;

    private const long OffsetSeconds = 330 * 60;

    /// <summary>Minutes since midnight, India time, of the minute that starts at the instant.</summary>
    public static int MinuteOfDay(long epochSeconds) => (int)(((epochSeconds + OffsetSeconds) % SecondsPerDay + SecondsPerDay) % SecondsPerDay / 60);

    /// <summary>The India calendar date of the instant.</summary>
    public static DateOnly DateOf(long epochSeconds) => DateOnly.FromDateTime(DateTime.UnixEpoch.AddSeconds(epochSeconds + OffsetSeconds));

    public static bool InRegularSession(int minuteOfDay) => minuteOfDay is >= OpenMinute and < CloseMinute;

    /// <summary>A regular-session minute or one of a special evening session: where a fetched bar may lie.</summary>
    public static bool InAnySession(int minuteOfDay) =>
        InRegularSession(minuteOfDay) || minuteOfDay is >= EveningSessionFromMinute and < EveningSessionToMinute;
}
