using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>Builds bars at India-time minutes of a date (the epoch of a bar is the start of its minute).</summary>
internal static class Minutes
{
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    public const int Open = 9 * 60 + 15;

    public static int At(int hour, int minute) => hour * 60 + minute;

    public static long Epoch(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, IndiaOffset).ToUnixTimeSeconds() + minuteOfDay * 60L;

    public static Bar Bar(DateOnly date, int minuteOfDay, double open, double high, double low, double close, long volume = 10) =>
        new(Epoch(date, minuteOfDay), open, high, low, close, volume);

    /// <summary>A bar with only a close (open, high and low are the close): enough for indicators that read one price.</summary>
    public static Bar Close(DateOnly date, int minuteOfDay, double close, long volume = 10) => Bar(date, minuteOfDay, close, close, close, close, volume);

    /// <summary>Bars at consecutive minutes from the open of the date, one per close.</summary>
    public static List<Bar> Closes(DateOnly date, params double[] closes) =>
        [.. closes.Select((c, i) => Close(date, Open + i, c))];
}
