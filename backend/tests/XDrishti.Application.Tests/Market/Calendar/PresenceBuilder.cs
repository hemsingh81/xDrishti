using XDrishti.Application.Abstractions.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Calendar;

/// <summary>
/// A universe of instruments with bars on some dates, summarised the way the database does it (<see cref="DayPresence"/>): per date how many instruments have bars,
/// how many of those have bars on both sides, and the median bar count. Every instrument has a bar on every date it is given.
/// </summary>
internal sealed class PresenceBuilder
{
    private readonly List<Dictionary<DateOnly, int>> _instruments = [];

    /// <summary>Weekdays from <paramref name="first"/> to <paramref name="last"/> for <paramref name="count"/> more instruments; <paramref name="has"/> decides per instrument number (0-based within this call) and date, <paramref name="bars"/> gives the bar count (375 by default).</summary>
    public PresenceBuilder Weekdays(int count, DateOnly first, DateOnly last, Func<int, DateOnly, bool>? has = null, Func<DateOnly, int>? bars = null)
    {
        for (var i = 0; i < count; i++)
        {
            var days = new Dictionary<DateOnly, int>();
            for (var d = first; d <= last; d = d.AddDays(1))
            {
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && (has?.Invoke(i, d) ?? true))
                {
                    days[d] = bars?.Invoke(d) ?? 375;
                }
            }

            _instruments.Add(days);
        }

        return this;
    }

    /// <summary>Gives the first <paramref name="count"/> instruments (of all added so far) bars on a date, however it falls.</summary>
    public PresenceBuilder On(DateOnly date, int count, int bars = 375)
    {
        foreach (var instrument in _instruments.Take(count))
        {
            instrument[date] = bars;
        }

        return this;
    }

    public DayPresence Build(bool pending = false)
    {
        var spans = _instruments.Where(i => i.Count > 0).Select(i => new InstrumentSpan(i.Keys.Min(), i.Keys.Max())).ToList();
        var days = _instruments
            .SelectMany(i => i.Select(d => (Date: d.Key, d.Value, First: i.Keys.Min(), Last: i.Keys.Max())))
            .GroupBy(x => x.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DayCount(
                g.Key,
                g.Count(),
                g.Count(x => x.Date > x.First && x.Date < x.Last),
                g.Select(x => x.Value).OrderBy(v => v).ElementAt((g.Count() - 1) / 2)))
            .ToList();
        return new DayPresence(pending, days, spans);
    }
}
