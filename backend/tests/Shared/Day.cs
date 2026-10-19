using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Tests.Shared;

/// <summary>
/// Builds the invented days of the fixtures (spec P3-03a, D12, and P3-03, D9) from rows written the way the spec writes them: <c>"09:44 99.00/100.50/99.00/100.50"</c> is a minute with its open, high, low and close.
/// A day is dense from its first to its last minute (09:15 to 15:29 unless stated), volume 1,000 per minute, and <b>a minute that is not listed repeats the previous close</b> as its open, high, low and close; a minute
/// declared absent is not in the list at all (and the next unlisted minute still repeats the last close that was there).
/// </summary>
internal sealed class Day
{
    public const long MinuteVolume = 1_000;

    public static readonly DateOnly Monday = new(2026, 3, 2);

    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    private readonly Dictionary<int, (double Open, double High, double Low, double Close)> _listed = [];

    private readonly HashSet<int> _absent = [];

    private readonly HashSet<int> _empty = [];

    private readonly int _first;

    private readonly int _last;

    private double _startPrice = double.NaN;

    private long _volume = MinuteVolume;

    private Day(DateOnly date, int first, int last)
    {
        Date = date;
        _first = first;
        _last = last;
    }

    public DateOnly Date { get; }

    /// <summary>A dense day on the date from <paramref name="first"/> to <paramref name="last"/> (both included), 09:15 to 15:29 when not given.</summary>
    public static Day Dense(DateOnly? date = null, string first = "09:15", string last = "15:29") => new(date ?? Monday, Minute(first), Minute(last));

    /// <summary>A dense day on <paramref name="date"/> that starts at the last close of <paramref name="previous"/> (the first minute of a later day repeats the close before it, P3-03d).</summary>
    public static Day After(Day previous, DateOnly date) => Dense(date).Start(previous.Bars()[^1].Close);

    /// <summary>The prefix P of P3-03 D9: 09:15 flat 99.00, the 09:20 minute high 100.00, the 09:25 minute low 98.00, so that the first 15-minute bar is O 99 H 100 L 98 C 99.</summary>
    public static Day P(DateOnly? date = null) => Dense(date).Start(99.00).Set("09:20 99.00/100.00/99.00/99.00").Set("09:25 99.00/99.00/98.00/99.00");

    /// <summary>The minute of the day of an "HH:MM" clock time.</summary>
    public static int Minute(string clock)
    {
        var parts = clock.Split(':');
        return int.Parse(parts[0], CultureInfo.InvariantCulture) * 60 + int.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    /// <summary>The price the first minute repeats when it is not listed.</summary>
    public Day Start(double price)
    {
        _startPrice = price;
        return this;
    }

    /// <summary>The volume of every non-empty minute of the day (P3-06, D10); <see cref="MinuteVolume"/> when not given.</summary>
    public Day Volume(long perMinute)
    {
        _volume = perMinute;
        return this;
    }

    /// <summary>Lists a minute: <c>"09:44 99.00/100.50/99.00/100.50"</c>.</summary>
    public Day Set(string row)
    {
        var space = row.IndexOf(' ', StringComparison.Ordinal);
        var minute = Minute(row[..space]);
        var prices = row[(space + 1)..].Split('/').Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        _listed[minute] = (prices[0], prices[1], prices[2], prices[3]);
        _absent.Remove(minute);
        return this;
    }

    /// <summary>Lists a minute with volume 0 (<c>(v0)</c> in the specs): it is in the list but holds no trade.</summary>
    public Day SetEmpty(string row)
    {
        Set(row);
        _empty.Add(Minute(row[..row.IndexOf(' ', StringComparison.Ordinal)]));
        return this;
    }

    /// <summary>Lists several minutes.</summary>
    public Day Set(params string[] rows)
    {
        foreach (var row in rows)
        {
            Set(row);
        }

        return this;
    }

    /// <summary>The B minute of P3-03 D9: <c>09:44 99.00/100.50/99.00/100.50</c>, which makes the first break of the opening range.</summary>
    public Day B() => Set("09:44 99.00/100.50/99.00/100.50");

    /// <summary>Removes the minutes from <paramref name="from"/> to <paramref name="to"/> (both included) from the list.</summary>
    public Day Absent(string from, string to)
    {
        for (var minute = Minute(from); minute <= Minute(to); minute++)
        {
            _absent.Add(minute);
            _listed.Remove(minute);
        }

        return this;
    }

    /// <summary>Removes one minute.</summary>
    public Day Absent(string at) => Absent(at, at);

    /// <summary>Removes every minute after <paramref name="at"/>.</summary>
    public Day EndsAfter(string at) => Absent(Clock(Minute(at) + 1), Clock(_last));

    /// <summary>Removes every minute before <paramref name="at"/>.</summary>
    public Day StartsAt(string at) => Absent(Clock(_first), Clock(Minute(at) - 1));

    /// <summary>The bars of the day in order.</summary>
    public Bar[] Bars()
    {
        var bars = new List<Bar>();
        var previous = _startPrice;
        for (var minute = _first; minute <= _last; minute++)
        {
            if (_absent.Contains(minute))
            {
                continue;
            }

            var (open, high, low, close) = _listed.TryGetValue(minute, out var row) ? row : (previous, previous, previous, previous);
            if (double.IsNaN(open))
            {
                throw new InvalidOperationException("The first minute of the day is not listed and the day has no start price.");
            }

            bars.Add(new Bar(Epoch(Date, minute), open, high, low, close, _empty.Contains(minute) ? 0 : _volume));
            previous = close;
        }

        return [.. bars];
    }

    /// <summary>The epoch second of the start of the minute (a minute of the day, India time) on the date.</summary>
    public static long Epoch(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, IndiaOffset).ToUnixTimeSeconds() + minuteOfDay * 60L;

    /// <summary>The "HH:MM" clock time of a minute of the day.</summary>
    public static string Clock(int minuteOfDay) => (minuteOfDay / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minuteOfDay % 60).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>The bars of several days in one list.</summary>
    public static Bar[] Join(params Day[] days) => [.. days.SelectMany(d => d.Bars())];
}
