namespace XDrishti.SimReader.Tests;

/// <summary>A hand-made trading day (2026-03-02, India time). Minutes are written "HH:mm"; a flat minute has open = high = low = close and volume 1000.</summary>
internal sealed class Day
{
    private static readonly DateTimeOffset Midnight = new(2026, 3, 2, 0, 0, 0, TimeSpan.FromHours(5.5));

    private readonly SortedDictionary<int, ReaderBar> _bars = [];

    public static int Minute(string hhmm) => (int.Parse(hhmm[..2], System.Globalization.CultureInfo.InvariantCulture) * 60) + int.Parse(hhmm[3..], System.Globalization.CultureInfo.InvariantCulture);

    public static long Epoch(string hhmm) => Midnight.ToUnixTimeSeconds() + (Minute(hhmm) * 60L);

    public Day Flat(string from, string to, decimal price)
    {
        for (var m = Minute(from); m <= Minute(to); m++)
        {
            Put(m, price, price, price, price, 1000m);
        }

        return this;
    }

    public Day Bar(string at, decimal o, decimal h, decimal l, decimal c, decimal volume = 1000m)
    {
        Put(Minute(at), o, h, l, c, volume);
        return this;
    }

    public Day Remove(string from, string to)
    {
        for (var m = Minute(from); m <= Minute(to); m++)
        {
            _bars.Remove(m);
        }

        return this;
    }

    public IReadOnlyList<ReaderBar> Build() => [.. _bars.Values];

    private void Put(int minute, decimal o, decimal h, decimal l, decimal c, decimal volume) =>
        _bars[minute] = new ReaderBar(Midnight.ToUnixTimeSeconds() + (minute * 60L), o, h, l, c, volume);

    /// <summary>The opening range: 09:15 to 09:29 flat 100 with 09:20 high 101 and 09:25 low 99, so OR high 101 and OR low 99; then flat 100 to 09:43.</summary>
    public static Day WithRange()
    {
        var d = new Day().Flat("09:15", "09:43", 100m);
        return d.Bar("09:20", 100m, 101m, 100m, 100m).Bar("09:25", 100m, 100m, 99m, 100m);
    }

    /// <summary>A BUY trigger: the 09:30 bar (09:30 to 09:44) closes 102.5 > 101 after a first bar that closed 100 (&lt;= 101); flat 102.5 afterwards.</summary>
    public static Day Buy() => WithRange().Bar("09:44", 100m, 103m, 100m, 102.5m).Flat("09:45", "15:29", 102.5m);

    /// <summary>A SELL trigger: the 09:30 bar closes 97.5 &lt; 99 after a first bar that closed 100 (&gt;= 99); flat 97.5 afterwards.</summary>
    public static Day Sell() => WithRange().Bar("09:44", 100m, 100m, 97m, 97.5m).Flat("09:45", "15:29", 97.5m);
}
