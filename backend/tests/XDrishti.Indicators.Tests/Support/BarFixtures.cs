using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>Named, deterministic 1-minute fixtures for the timeframe tests (nothing random: prices are a function of the date and the minute).</summary>
internal static class BarFixtures
{
    public static readonly DateOnly Mon = new(2026, 3, 2);
    public static readonly DateOnly Tue = new(2026, 3, 3);
    public static readonly DateOnly Wed = new(2026, 3, 4);
    public static readonly DateOnly Sat = new(2026, 3, 7);
    public static readonly DateOnly Sun = new(2026, 3, 8);

    public static IReadOnlyList<string> Names { get; } =
    [
        "normal day", "missing minutes", "partial last hour", "last ten and thirty minute buckets", "muhurat evening", "saturday session", "three consecutive days", "short day", "missing close",
    ];

    public static List<Bar> Get(string name) => name switch
    {
        "normal day" => Walk(Mon, Minutes.Open, 375),
        "missing minutes" => Walk(Mon, Minutes.Open, 375, skip: m => m is 556 or 560 or 561 or (>= 600 and < 615) or 700 || m == 929 - 20),
        "partial last hour" => Walk(Mon, Minutes.At(14, 50), 40),
        "last ten and thirty minute buckets" => Walk(Mon, Minutes.At(15, 5), 25),
        "muhurat evening" => Walk(Sun, Minutes.At(18, 15), 60),
        "saturday session" => Walk(Sat, Minutes.Open, 30),
        "three consecutive days" => [.. Walk(Mon, Minutes.Open, 375), .. Walk(Tue, Minutes.Open, 375, skip: m => m % 17 == 0), .. Walk(Wed, Minutes.Open, 375)],
        "short day" => Walk(Mon, Minutes.Open, 285),
        "missing close" => Walk(Mon, Minutes.Open, 375, skip: m => m == Minutes.At(15, 29)).Concat(Walk(Tue, Minutes.Open, 375)).ToList(),
        _ => throw new ArgumentException("Unknown fixture " + name),
    };

    /// <summary>A walk of <paramref name="count"/> minutes from <paramref name="startMinute"/>, minus the skipped minutes; prices and volumes depend only on the date and the minute.</summary>
    public static List<Bar> Walk(DateOnly date, int startMinute, int count, Func<int, bool>? skip = null)
    {
        var bars = new List<Bar>(count);
        for (var i = 0; i < count; i++)
        {
            var minute = startMinute + i;
            if (skip?.Invoke(minute) == true)
            {
                continue;
            }

            var seed = date.DayNumber * 31 + minute;
            var wave = Math.Sin(seed * 0.37) * 5;
            var open = 1_000 + wave + (seed % 7);
            var close = 1_000 + Math.Sin((seed + 1) * 0.37) * 5 + ((seed + 1) % 7);
            var high = Math.Max(open, close) + (seed % 5) * 0.25;
            var low = Math.Min(open, close) - (seed % 3) * 0.25;
            var volume = seed % 11 == 0 ? 0 : 100 + (seed * 37 % 900);
            bars.Add(Minutes.Bar(date, minute, open, high, low, close, volume));
        }

        return bars;
    }
}
