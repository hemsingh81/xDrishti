using XDrishti.Domain.Market.Bars;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>Builds 1-minute bars at India-time minutes of a date (the epoch of a bar is the start of its minute) and names a few dates with the weekday they fall on, worked out from a calendar, not from the library.</summary>
internal static class Clock
{
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    /// <summary>09:15, the first minute of the session.</summary>
    public const int Open = 9 * 60 + 15;

    public static readonly DateOnly Mon = new(2026, 3, 2);

    public static readonly DateOnly Tue = new(2026, 3, 3);

    public static readonly DateOnly Wed = new(2026, 3, 4);

    public static readonly DateOnly Thu = new(2026, 3, 5);

    public static readonly DateOnly Fri = new(2026, 3, 6);

    /// <summary>The minute of the day at a clock time: <c>At(9, 45)</c> is 585.</summary>
    public static int At(int hour, int minute) => hour * 60 + minute;

    /// <summary>The epoch second of the start of the minute (a minute of the day, India time) on the date.</summary>
    public static long Epoch(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, IndiaOffset).ToUnixTimeSeconds() + minuteOfDay * 60L;

    public static Bar Bar(DateOnly date, int minuteOfDay, double open, double high, double low, double close, long volume = 10) =>
        new(Epoch(date, minuteOfDay), open, high, low, close, volume);

    /// <summary>A bar with only a close: open, high and low are the close too.</summary>
    public static Bar Close(DateOnly date, int minuteOfDay, double close, long volume = 10) => Bar(date, minuteOfDay, close, close, close, close, volume);

    /// <summary>A bar with an open and a close (the high and the low are the larger and the smaller of the two).</summary>
    public static Bar OpenClose(DateOnly date, int minuteOfDay, double open, double close, long volume = 10) =>
        Bar(date, minuteOfDay, open, Math.Max(open, close), Math.Min(open, close), close, volume);

    /// <summary>Bars at consecutive minutes from 09:15 of the date, one per close.</summary>
    public static Bar[] Closes(DateOnly date, params double[] closes) => [.. closes.Select((c, i) => Close(date, Open + i, c))];
}
