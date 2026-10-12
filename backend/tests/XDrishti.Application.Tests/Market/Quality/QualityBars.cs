using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Quality;

/// <summary>Calm 1-minute bars for the quality tests: price 100.00 to 100.02, high and low a cent outside.</summary>
internal static class QualityBars
{
    public static readonly DateOnly Mon = new(2026, 10, 5);
    public static readonly DateOnly Tue = new(2026, 10, 6);
    public static readonly DateOnly Wed = new(2026, 10, 7);
    public static readonly DateOnly Thu = new(2026, 10, 8);
    public static readonly DateOnly Fri = new(2026, 10, 9);

    public static readonly DateOnly[] Window = [Mon, Tue, Wed, Thu, Fri];

    public static long Epoch(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds() + minuteOfDay * 60L;

    public static Bar Calm(DateOnly date, int minuteOfDay)
    {
        var open = 100 + 0.01 * (minuteOfDay % 3);
        var close = 100 + 0.01 * ((minuteOfDay + 1) % 3);
        return new Bar(Epoch(date, minuteOfDay), Math.Round(open, 2), Math.Round(Math.Max(open, close) + 0.01, 2), Math.Round(Math.Min(open, close) - 0.01, 2), Math.Round(close, 2), 10);
    }

    /// <summary>A regular session minus the minutes the predicate leaves out.</summary>
    public static List<Bar> Session(DateOnly date, Func<int, bool>? skip = null) =>
        [.. Enumerable.Range(SessionClock.OpenMinute, SessionClock.RegularBars).Where(m => skip?.Invoke(m) != true).Select(m => Calm(date, m))];

    public static List<Bar> Sessions(params DateOnly[] dates) => [.. dates.SelectMany(d => Session(d))];

    public static List<Bar> FullWindow() => Sessions(Window);
}
