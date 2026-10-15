namespace XDrishti.Indicators;

/// <summary>
/// India calendar arithmetic on epoch seconds with integers only (India time is a fixed UTC+05:30, <c>IndiaTime.Offset</c>): the trading date a bar belongs to and its minute of the day. A bar's
/// <c>EpochSeconds</c> is the start of its minute (or of its bucket), so both are those of the start.
/// </summary>
internal static class IndiaDays
{
    /// <summary>UTC+05:30 in seconds; a test pins it to <c>IndiaTime.Offset</c>.</summary>
    public const long OffsetSeconds = 19_800;

    public const long SecondsPerDay = 86_400;

    /// <summary>The India date as a day number (days since 1970-01-01 in India time); two bars are in the same session date exactly when the numbers are equal.</summary>
    public static long DayOf(long epochSeconds) => FloorDiv(epochSeconds + OffsetSeconds, SecondsPerDay);

    /// <summary>Minutes since India midnight of the start of the minute.</summary>
    public static int MinuteOfDay(long epochSeconds) => (int)(FloorMod(epochSeconds + OffsetSeconds, SecondsPerDay) / 60);

    /// <summary>The epoch second of India midnight of the day number.</summary>
    public static long MidnightOf(long day) => day * SecondsPerDay - OffsetSeconds;

    public static long FloorDiv(long a, long b)
    {
        var q = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
    }

    public static long FloorMod(long a, long b) => a - FloorDiv(a, b) * b;
}
