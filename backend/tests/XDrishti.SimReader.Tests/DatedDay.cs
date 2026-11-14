namespace XDrishti.SimReader.Tests;

/// <summary>
/// A hand-made trading day on any date (India time). A minute is set with <see cref="Bar"/>; <see cref="Build"/> fills every minute from 09:15 to 15:29 that is not set by repeating the previous close
/// (volume 1000), so the first minute (09:15) must be set. Used by the multi-session readers.
/// </summary>
internal sealed class DatedDay(DateOnly date)
{
    private const int First = (9 * 60) + 15;
    private const int Last = (15 * 60) + 29;

    private readonly SortedDictionary<int, ReaderBar> _bars = [];

    private long Midnight => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();

    public DateOnly Date => date;

    public static int Minute(string hhmm) => (int.Parse(hhmm[..2], System.Globalization.CultureInfo.InvariantCulture) * 60) + int.Parse(hhmm[3..], System.Globalization.CultureInfo.InvariantCulture);

    public long Epoch(string hhmm) => Midnight + (Minute(hhmm) * 60L);

    public DatedDay Bar(string at, decimal o, decimal h, decimal l, decimal c, decimal volume = 1000m)
    {
        _bars[Minute(at)] = new ReaderBar(Midnight + (Minute(at) * 60L), o, h, l, c, volume);
        return this;
    }

    /// <summary>A flat minute (open = high = low = close).</summary>
    public DatedDay Flat(string at, decimal price, decimal volume = 1000m) => Bar(at, price, price, price, price, volume);

    /// <summary>Removes the minutes from <paramref name="from"/> to <paramref name="to"/> after the fill (a minute without a bar).</summary>
    public DatedDay Hole(string from, string to)
    {
        _holes.Add((Minute(from), Minute(to)));
        return this;
    }

    private readonly List<(int From, int To)> _holes = [];

    public IReadOnlyList<ReaderBar> Bars()
    {
        var result = new List<ReaderBar>();
        var previous = _bars[First].Close;
        for (var m = First; m <= Last; m++)
        {
            var bar = _bars.TryGetValue(m, out var set) ? set : new ReaderBar(Midnight + (m * 60L), previous, previous, previous, previous, 1000m);
            previous = bar.Close;
            if (!_holes.Any(h => m >= h.From && m <= h.To))
            {
                result.Add(bar);
            }
        }

        return result;
    }

    public ReaderSession Build() => new(date, Bars());

    /// <summary>The session reflected about <paramref name="axis"/>: every price becomes axis x 2 minus the price, high and low swap.</summary>
    public static ReaderSession Mirror(ReaderSession session, decimal axis) =>
        new(session.Date, [.. session.Bars.Select(b => b with { Open = (2 * axis) - b.Open, High = (2 * axis) - b.Low, Low = (2 * axis) - b.High, Close = (2 * axis) - b.Close })]);
}
