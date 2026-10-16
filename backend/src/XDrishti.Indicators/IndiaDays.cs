namespace XDrishti.Indicators;

/// <summary>
/// India calendar arithmetic on epoch seconds with integers only (India time is a fixed UTC+05:30, <c>IndiaTime.Offset</c>): the trading date a bar belongs to and its minute of the day. A bar's
/// <c>EpochSeconds</c> is the start of its minute (or of its bucket), so both are those of the start. The one place of this library that knows how a day number relates to a <see cref="DateOnly"/>, to the weekday
/// and to the month; a day number is always a <see cref="long"/> and no function here throws, whatever the epoch (a feed that sends milliseconds for seconds gives dates thousands of years away).
/// </summary>
internal static class IndiaDays
{
    /// <summary>UTC+05:30 in seconds; a test pins it to <c>IndiaTime.Offset</c>.</summary>
    public const long OffsetSeconds = 19_800;

    public const long SecondsPerDay = 86_400;

    /// <summary><see cref="DateOnly.DayNumber"/> of 1970-01-01: a day number of this class (days since 1970-01-01) plus this is the <see cref="DateOnly"/> day number.</summary>
    public const int UnixEpochDayNumber = 719_162;

    /// <summary>The Gregorian calendar repeats exactly every 400 years: 146,097 days, which is also a whole number of weeks (20,871).</summary>
    private const long GregorianCycleDays = 146_097;

    /// <summary>1970-01-01 was a Thursday, three days after a Monday: (days since 1970-01-01 + 3) modulo 7 is the number of days since the Monday of the week.</summary>
    private const int DaysFromMondayAtEpoch = 3;

    /// <summary>The India date as a day number (days since 1970-01-01 in India time); two bars are in the same session date exactly when the numbers are equal.</summary>
    public static long DayOf(long epochSeconds) => FloorDiv(epochSeconds + OffsetSeconds, SecondsPerDay);

    /// <summary>Minutes since India midnight of the start of the minute.</summary>
    public static int MinuteOfDay(long epochSeconds) => (int)(FloorMod(epochSeconds + OffsetSeconds, SecondsPerDay) / 60);

    /// <summary>The epoch second of India midnight of the day number.</summary>
    public static long MidnightOf(long day) => day * SecondsPerDay - OffsetSeconds;

    /// <summary>The day number (days since 1970-01-01) of a date.</summary>
    public static long DayNumberOf(DateOnly date) => date.DayNumber - UnixEpochDayNumber;

    /// <summary>The date of a day number; false when it lies outside the range of <see cref="DateOnly"/> (years 1 to 9999): a garbage epoch has no date.</summary>
    public static bool TryDateOf(long day, out DateOnly date)
    {
        var number = day + UnixEpochDayNumber;
        if (number < DateOnly.MinValue.DayNumber || number > DateOnly.MaxValue.DayNumber)
        {
            date = default;
            return false;
        }

        date = DateOnly.FromDayNumber((int)number);
        return true;
    }

    /// <summary>How many days the day is after the Monday of its week: 0 for a Monday … 6 for a Sunday.</summary>
    public static int DaysSinceMonday(long day) => (int)FloorMod(day + DaysFromMondayAtEpoch, 7);

    /// <summary>The first day of the month that holds the day and the first day of the month after it, as day numbers. The Gregorian rules are continued without end, so the answer exists for every day.</summary>
    public static void MonthBounds(long day, out long first, out long next)
    {
        // The calendar repeats every 400 years: work in the cycle that starts in 1970 (far from both ends of the range of DateOnly) and shift the answer back by whole cycles.
        var inCycle = FloorMod(day, GregorianCycleDays);
        var shift = day - inCycle;
        var date = DateOnly.FromDayNumber((int)inCycle + UnixEpochDayNumber);
        var month = new DateOnly(date.Year, date.Month, 1);
        first = shift + DayNumberOf(month);
        next = shift + DayNumberOf(month.AddMonths(1));
    }

    public static long FloorDiv(long a, long b)
    {
        var q = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
    }

    public static long FloorMod(long a, long b) => a - FloorDiv(a, b) * b;
}
