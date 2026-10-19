namespace XDrishti.SimOracle;

/// <summary>India time (UTC+05:30, no daylight saving) arithmetic on epoch seconds, written from the rules alone.</summary>
public static class OracleClock
{
    private const long OffsetSeconds = 19800;
    private const long DaySeconds = 86400;
    private const int UnixDayNumber = 719162;

    /// <summary>The India date of an instant.</summary>
    public static DateOnly DateOf(long epochSeconds)
    {
        long days = FloorDiv(epochSeconds + OffsetSeconds, DaySeconds);
        return DateOnly.FromDayNumber((int)(days + UnixDayNumber));
    }

    /// <summary>The India minute of the day (0 to 1439) of an instant.</summary>
    public static int MinuteOfDay(long epochSeconds)
    {
        long shifted = epochSeconds + OffsetSeconds;
        long rem = shifted - (FloorDiv(shifted, DaySeconds) * DaySeconds);
        return (int)(rem / 60);
    }

    /// <summary>The instant of a minute of the day on an India date.</summary>
    public static long Epoch(DateOnly date, int minuteOfDay) =>
        ((date.DayNumber - UnixDayNumber) * DaySeconds) + (minuteOfDay * 60L) - OffsetSeconds;

    private static long FloorDiv(long a, long b)
    {
        long q = a / b;
        return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q;
    }
}
