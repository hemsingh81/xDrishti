namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// Seeded random daily series for the market-context tests: weekdays with a few missing days (holidays), an index that moves through phases of a different drift and volatility (so every trend and every
/// volatility level occurs), a VIX that follows the volatility, rounded to two decimals so that values tie, and a breadth in percent. Depending on the seed the VIX is missing for stretches, starts late or is
/// absent, and the breadth is sometimes absent. The same seed always gives the same days; no value is exactly flat, so a comparison is never decided by a rounding tie.
/// </summary>
internal static class ContextSeriesGenerator
{
    /// <summary>The Monday the small hand examples start on.</summary>
    public static DateOnly Monday { get; } = new(2026, 3, 2);

    private static readonly double[] Drifts = [0.0035, -0.0035, 0.0, 0.0, 0.0008, -0.0008];

    private static readonly double[] Sigmas = [0.004, 0.009, 0.02];

    public static List<ContextDay> Random(int seed, int minDays = 300, int maxDays = 900)
    {
        var random = new Random(seed);
        var count = random.Next(minDays, maxDays + 1);
        var days = new List<ContextDay>(count);
        var date = new DateOnly(2018, 1, 1).AddDays(random.Next(0, 700));
        var close = 8_000 + random.NextDouble() * 12_000;
        var vix = 14.0;
        var drift = 0.0;
        var sigma = Sigmas[0];
        var phaseLeft = 0;
        var vixMode = seed % 6;
        var vixGapLeft = 0;
        var firstVixDay = vixMode == 1 ? random.Next(60, 200) : 0;

        while (days.Count < count)
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || random.NextDouble() < 0.03)
            {
                date = date.AddDays(1);
                continue;
            }

            if (phaseLeft == 0)
            {
                phaseLeft = random.Next(20, 90);
                drift = Drifts[random.Next(Drifts.Length)];
                sigma = Sigmas[random.Next(Sigmas.Length)];
            }

            phaseLeft--;
            var previous = close;
            close = Math.Max(1, previous * (1 + drift + sigma * Gauss(random)));
            var high = Math.Max(previous, close) * (1 + sigma * random.NextDouble() * 0.7);
            var low = Math.Min(previous, close) * (1 - sigma * random.NextDouble() * 0.7);

            vix = Math.Clamp(vix + 0.15 * (8 + sigma * 1_400 - vix) + Gauss(random) * 0.8, 8, 90);
            double? vixClose = Math.Round(vix, 2);
            if (vixGapLeft > 0)
            {
                vixGapLeft--;
                vixClose = null;
            }
            else if (random.NextDouble() < 0.02)
            {
                vixGapLeft = random.Next(0, 6);
                vixClose = null;
            }

            if (vixMode == 0 || days.Count < firstVixDay)
            {
                vixClose = null;
            }

            double? breadth = seed % 4 == 0 || random.NextDouble() < 0.1 ? null : Math.Round(Math.Clamp(50 + Gauss(random) * 25, 0, 100), 1);
            days.Add(new ContextDay(date, high, low, close, vixClose, breadth));
            date = date.AddDays(1);
        }

        return days;
    }

    /// <summary>The n-th weekday from <see cref="Monday"/> (0 is the Monday itself), skipping Saturdays and Sundays: consecutive trading days for the hand examples.</summary>
    public static DateOnly Weekday(int n) => Weekday(Monday, n);

    /// <summary>The n-th weekday from a Monday (0 is the Monday itself), skipping Saturdays and Sundays.</summary>
    public static DateOnly Weekday(DateOnly monday, int n) => monday.AddDays(n / 5 * 7 + n % 5);

    private static double Gauss(Random random) =>
        Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
}
