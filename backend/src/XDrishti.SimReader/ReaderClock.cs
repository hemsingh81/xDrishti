namespace XDrishti.SimReader;

/// <summary>India-time arithmetic on bar starts, shared by the readers (no clock, no calendar).</summary>
internal static class ReaderClock
{
    public const int SecondsPerMinute = 60;
    public const int IndiaOffsetSeconds = 19800;
    public const int SessionOpen = (9 * 60) + 15;
    public const int SessionClose = (15 * 60) + 30;

    public static int MinuteOfDay(ReaderBar bar) => (int)(((bar.EpochSeconds + IndiaOffsetSeconds) % 86400) / SecondsPerMinute);

    public static long DayOf(ReaderBar bar) => (bar.EpochSeconds + IndiaOffsetSeconds) / 86400;

    public static int Minutes(TimeOnly time) => (time.Hour * 60) + time.Minute;

    /// <summary>The caller's list must be one India date in ascending order; minutes outside 09:15 to 15:30 are not part of the session and are left out.</summary>
    public static List<ReaderBar> SessionBars(IReadOnlyList<ReaderBar> input)
    {
        var result = new List<ReaderBar>(input.Count);
        for (var i = 0; i < input.Count; i++)
        {
            if (DayOf(input[i]) != DayOf(input[0]))
            {
                throw new ArgumentException("The bars must be of one India date.", nameof(input));
            }

            if (i > 0 && input[i].EpochSeconds <= input[i - 1].EpochSeconds)
            {
                throw new ArgumentException("The bars must be strictly ascending.", nameof(input));
            }

            var minute = MinuteOfDay(input[i]);
            if (minute >= SessionOpen && minute < SessionClose)
            {
                result.Add(input[i]);
            }
        }

        return result;
    }
}
