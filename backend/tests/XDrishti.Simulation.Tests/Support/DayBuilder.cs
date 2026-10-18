using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// Turns the rows of the fixtures of the spec (D9) into a list of 1-minute bars. A day is dense from 09:15 to 15:29 (or to <see cref="Until"/>); a row <c>HH:MM O/H/L/C</c> is one listed minute; <b>a minute not listed repeats the
/// previous close as its open, high, low and close</b>; <see cref="Absent"/> removes minutes from the list. Volume is 1,000 per minute. Every price of the fixtures is a multiple of 0.25, so the arithmetic is exact.
/// </summary>
internal sealed class DayBuilder
{
    /// <summary>2026-03-02, a Monday: the date of every fixture unless it says otherwise.</summary>
    public static readonly DateOnly Monday = new(2026, 3, 2);

    private readonly DateOnly _date;

    private readonly SortedDictionary<int, (double Open, double High, double Low, double Close)> _listed = [];

    private readonly HashSet<int> _absent = [];

    private int _lastMinute = SessionClock.CloseMinute - 1;

    private DayBuilder(DateOnly date) => _date = date;

    /// <summary>Starts a day with the prefix P of the spec: 09:15 flat at 99.00, then the minutes 09:20 and 09:25 that make the opening range 98.00 to 100.00.</summary>
    public static DayBuilder Prefix(DateOnly? date = null) =>
        new DayBuilder(date ?? Monday).Row("09:15 99.00").Row("09:20 99.00/100.00/99.00/99.00").Row("09:25 99.00/99.00/98.00/99.00");

    /// <summary>The minute of the trigger bar of the B days: <c>09:44 99.00/100.50/99.00/100.50</c>.</summary>
    public DayBuilder B() => Row("09:44 99.00/100.50/99.00/100.50");

    /// <summary>Lists a minute: <c>HH:MM O/H/L/C</c>, or <c>HH:MM P</c> for a flat minute.</summary>
    public DayBuilder Row(string row)
    {
        var parts = row.Split(' ', 2);
        var prices = parts[1].Split('/').Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        _listed[MinuteOfDay(parts[0])] = prices.Length == 1 ? (prices[0], prices[0], prices[0], prices[0]) : (prices[0], prices[1], prices[2], prices[3]);
        _absent.Remove(MinuteOfDay(parts[0]));
        return this;
    }

    /// <summary>Removes the minutes from <paramref name="from"/> to <paramref name="to"/>, both included, from the list.</summary>
    public DayBuilder Absent(string from, string to)
    {
        for (var minute = MinuteOfDay(from); minute <= MinuteOfDay(to); minute++)
        {
            _absent.Add(minute);
            _listed.Remove(minute);
        }

        return this;
    }

    /// <summary>Ends the day after the minute <paramref name="last"/> (included).</summary>
    public DayBuilder Until(string last)
    {
        _lastMinute = MinuteOfDay(last);
        return this;
    }

    /// <summary>The minutes of the day, ascending.</summary>
    public List<Bar> Build()
    {
        var bars = new List<Bar>();
        var previousClose = double.NaN;
        for (var minute = SessionClock.OpenMinute; minute <= _lastMinute; minute++)
        {
            var (open, high, low, close) = _listed.TryGetValue(minute, out var listed) ? listed : (previousClose, previousClose, previousClose, previousClose);
            previousClose = close;
            if (!_absent.Contains(minute))
            {
                bars.Add(new Bar(Epoch(_date, minute), open, high, low, close, 1_000));
            }
        }

        return bars;
    }

    /// <summary>The minute of the India day of <c>HH:MM</c>.</summary>
    public static int MinuteOfDay(string time) => int.Parse(time[..2], CultureInfo.InvariantCulture) * 60 + int.Parse(time[3..], CultureInfo.InvariantCulture);

    /// <summary>The instant (seconds since 1970-01-01 UTC) at which the minute <paramref name="minuteOfDay"/> of an India date starts.</summary>
    public static long Epoch(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds() - (330 * 60) + (minuteOfDay * 60L);

    /// <summary>The instant at which <c>HH:MM</c> of an India date starts.</summary>
    public static long Epoch(DateOnly date, string time) => Epoch(date, MinuteOfDay(time));

    /// <summary>The position in <paramref name="bars"/> of the minute that starts at <c>HH:MM</c> of an India date.</summary>
    public static int IndexOf(IReadOnlyList<Bar> bars, DateOnly date, string time)
    {
        var epoch = Epoch(date, time);
        for (var i = 0; i < bars.Count; i++)
        {
            if (bars[i].EpochSeconds == epoch)
            {
                return i;
            }
        }

        throw new ArgumentException("No minute at " + time, nameof(time));
    }
}
