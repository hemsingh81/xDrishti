using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// 1-minute bars over several days or weeks and the dates the market was closed on weekdays (the calendar holidays, which have no bars): what the weekly and monthly tests feed. Bars are in time order.
/// </summary>
internal sealed record PeriodSeries(List<Bar> Bars, HashSet<DateOnly> Holidays);

/// <summary>Named, deterministic 1-minute fixtures for the timeframe tests (nothing random: prices are a function of the date and the minute).</summary>
internal static class BarFixtures
{
    public static readonly DateOnly Mon = new(2026, 3, 2);
    public static readonly DateOnly Tue = new(2026, 3, 3);
    public static readonly DateOnly Wed = new(2026, 3, 4);
    public static readonly DateOnly Thu = new(2026, 3, 5);
    public static readonly DateOnly Fri = new(2026, 3, 6);
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

    /// <summary>
    /// A walk of <paramref name="count"/> minutes from <paramref name="startMinute"/>, minus the skipped minutes; prices and volumes depend only on the date, the minute and the <paramref name="salt"/> (0 for the
    /// named fixtures; the random series pass their seed so that two seeds do not repeat the same prices).
    /// </summary>
    public static List<Bar> Walk(DateOnly date, int startMinute, int count, Func<int, bool>? skip = null, int salt = 0)
    {
        var bars = new List<Bar>(count);
        for (var i = 0; i < count; i++)
        {
            var minute = startMinute + i;
            if (skip?.Invoke(minute) == true)
            {
                continue;
            }

            var seed = date.DayNumber * 31 + minute + salt * 7_919;
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

    /// <summary>
    /// A day told by two bars, at 09:15 and at 15:29 (the minute that closes a regular day): its open, high, low, close and volume are those you give (<paramref name="low"/> at most both prices, <paramref name="high"/> at least
    /// both), it has 2 bars and is incomplete. The bars of a hand-calculated week or month.
    /// </summary>
    public static List<Bar> Brief(DateOnly date, double open, double high, double low, double close, long volume) =>
    [
        Minutes.Bar(date, Minutes.Open, open, high, low, open, volume),
        Minutes.Bar(date, Minutes.At(15, 29), close, close, close, close, 0),
    ];

    /// <summary>The names of the weekly and monthly fixtures of <see cref="GetPeriods"/>.</summary>
    public static IReadOnlyList<string> PeriodNames { get; } =
    [
        "holiday friday", "month ends on a weekend", "month boundary inside a week", "saturday session", "half missing day", "single session week", "holiday only week", "year end", "leap february",
        "february of a common year", "starts mid-week at the open", "starts mid-week mid-day", "muhurat sunday", "missing friday close",
    ];

    public static PeriodSeries GetPeriods(string name) => name switch
    {
        // Friday 6 March is a holiday; the week after has all five sessions; the Monday after it closes the second week when no calendar is given.
        "holiday friday" => new(Weekdays(Mon, new DateOnly(2026, 3, 16), Fri), [Fri]),
        // January 2026 ends on a Saturday: Friday the 30th is its last session.
        "month ends on a weekend" => new(Weekdays(new DateOnly(2026, 1, 26), new DateOnly(2026, 2, 6)), []),
        // Tuesday 31 March is the last day of March, in the middle of the week of the 30th.
        "month boundary inside a week" => new(Weekdays(new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 8)), []),
        "saturday session" => new(Ordered(Weekdays(Mon, new DateOnly(2026, 3, 11)), Walk(Sat, Minutes.Open, 30)), []),
        "half missing day" => new(Ordered(Weekdays(Mon, new DateOnly(2026, 3, 16), Wed), Walk(Wed, Minutes.Open, 375, skip: m => m % 2 == 0)), []),
        // The week of 9 March has the Wednesday only.
        "single session week" => new(Ordered(Weekdays(Mon, Fri), Walk(new DateOnly(2026, 3, 11), Minutes.Open, 375), Weekdays(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 20))),
            [new(2026, 3, 9), new(2026, 3, 10), new(2026, 3, 12), new(2026, 3, 13)]),
        // The whole week of 9 March is closed.
        "holiday only week" => new(Ordered(Weekdays(Mon, Fri), Weekdays(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 20))),
            [new(2026, 3, 9), new(2026, 3, 10), new(2026, 3, 11), new(2026, 3, 12), new(2026, 3, 13)]),
        // Thursday 31 December 2026 is the last session of the year when Friday 1 January is a holiday; the week of 28 December has both years in it.
        "year end" => new(Weekdays(new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 6), new DateOnly(2027, 1, 1)), [new(2027, 1, 1)]),
        // February 2028 has 29 days, a Tuesday.
        "leap february" => new(Weekdays(new DateOnly(2028, 2, 21), new DateOnly(2028, 3, 3)), []),
        // February 2027 has 28 days and ends on a Sunday.
        "february of a common year" => new(Weekdays(new DateOnly(2027, 2, 22), new DateOnly(2027, 3, 2)), []),
        "starts mid-week at the open" => new(Weekdays(Wed, new DateOnly(2026, 3, 16)), []),
        "starts mid-week mid-day" => new(Ordered(Walk(Wed, Minutes.At(11, 0), 270), Weekdays(Thu, new DateOnly(2026, 3, 16))), []),
        // Sunday 8 November 2026 is a Muhurat evening: the last session of the week that started on Monday 2 November.
        "muhurat sunday" => new(Ordered(Weekdays(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 11)), Walk(new DateOnly(2026, 11, 8), Minutes.At(18, 15), 60)), []),
        // The 15:29 minute of the last session of the week is missing: the day, and so the week, can only be closed by the next date.
        "missing friday close" => new(Ordered(Weekdays(Mon, new DateOnly(2026, 3, 16), Fri), Walk(Fri, Minutes.Open, 375, skip: m => m == Minutes.At(15, 29))), []),
        _ => throw new ArgumentException("Unknown period fixture " + name),
    };

    /// <summary>
    /// A seeded series of <paramref name="calendarDays"/> consecutive India dates from a random day between mid-2026 and late 2028 (so year ends and a leap February occur): weekday holidays (7%), full sessions, half-missing days,
    /// days that end early, start late, miss the 15:29 minute or hold a few bars, a Saturday session after one Saturday in four and a Muhurat-style Sunday evening after one Sunday in twenty. The first day may start mid-day.
    /// The same seed always gives the same series. The builder tests need what this produces — mostly whole 375-minute sessions, so that weeks and months are complete and their <c>Bars</c> are exact, with the defects
    /// of the real calendar between them. <see cref="SeriesGenerator.Thin"/> is the cheap generator for the frame tests (a few bars a day, hundreds of weeks); it would leave the complete periods untested here.
    /// </summary>
    public static PeriodSeries RandomPeriods(int seed, int calendarDays)
    {
        var random = new Random(seed);
        var date = new DateOnly(2026, 6, 1).AddDays(random.Next(0, 900));
        var bars = new List<Bar>();
        var holidays = new HashSet<DateOnly>();
        for (var i = 0; i < calendarDays; i++, date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                if (random.NextDouble() < 0.25)
                {
                    bars.AddRange(Walk(date, Minutes.Open, random.Next(10, 120), salt: seed));
                }
            }
            else if (date.DayOfWeek == DayOfWeek.Sunday)
            {
                if (random.NextDouble() < 0.05)
                {
                    bars.AddRange(Walk(date, Minutes.At(18, 15), 60, salt: seed));
                }
            }
            else if (random.NextDouble() < 0.07)
            {
                holidays.Add(date);
            }
            else
            {
                bars.AddRange(RandomSession(random, date, seed, i == 0));
            }
        }

        return new PeriodSeries(bars, holidays);
    }

    private static List<Bar> RandomSession(Random random, DateOnly date, int seed, bool first)
    {
        var roll = first && random.NextDouble() < 0.5 ? 0.9 : random.NextDouble();
        if (roll < 0.62)
        {
            return Walk(date, Minutes.Open, 375, salt: seed);
        }

        if (roll < 0.74)
        {
            return Walk(date, Minutes.Open, 375, skip: _ => random.NextDouble() < 0.3, salt: seed);
        }

        if (roll < 0.82)
        {
            return Walk(date, Minutes.Open, random.Next(100, 375), salt: seed);
        }

        if (roll < 0.90)
        {
            var start = random.Next(Minutes.Open + 1, Minutes.At(15, 29));
            return Walk(date, start, SessionClock.CloseMinute - start, salt: seed);
        }

        if (roll < 0.95)
        {
            return Walk(date, Minutes.Open, 375, skip: m => m == Minutes.At(15, 29), salt: seed);
        }

        return Walk(date, Minutes.Open, random.Next(1, 6), salt: seed);
    }

    /// <summary>Full regular sessions (09:15 to 15:29) on every weekday from <paramref name="from"/> to <paramref name="to"/>, both included, except the dates in <paramref name="except"/>.</summary>
    private static List<Bar> Weekdays(DateOnly from, DateOnly to, params DateOnly[] except)
    {
        var bars = new List<Bar>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !except.Contains(date))
            {
                bars.AddRange(Walk(date, Minutes.Open, 375));
            }
        }

        return bars;
    }

    private static List<Bar> Ordered(params List<Bar>[] parts)
    {
        var bars = parts.SelectMany(p => p).ToList();
        bars.Sort((a, b) => a.EpochSeconds.CompareTo(b.EpochSeconds));
        return bars;
    }
}
