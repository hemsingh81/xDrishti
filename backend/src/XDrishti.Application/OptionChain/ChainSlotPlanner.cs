using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Domain.Market;

namespace XDrishti.Application.OptionChain;

/// <summary>
/// Which requests in which slot, pure (P14-02 criterion 3). The slots of a trading day run from 09:20 to 15:30 India time every <c>IntervalMinutes</c> (75 at 5 minutes). The expiry lists are asked once per underlying from 09:15. The stock pass starts at 14:30 and must end by 15:25:
/// what is not asked by then is a <c>window_end</c> gap. Expiries are chosen from the lists: an index by its rule (the next weekly ones and the next month-end ones), a stock the next few.
/// </summary>
internal static class ChainSlotPlanner
{
    public static readonly TimeOnly PrepStart = new(9, 15);

    public static readonly TimeOnly FirstSlot = new(9, 20);

    public static readonly TimeOnly LastSlot = new(15, 30);

    public static readonly TimeOnly StockPassStart = new(14, 30);

    public static readonly TimeOnly StockPassEnd = new(15, 25);

    public static DateTimeOffset At(DateOnly date, TimeOnly time) => new(date.ToDateTime(time), IndiaTime.Offset);

    /// <summary>The slot instants of a day, ascending.</summary>
    public static IReadOnlyList<DateTimeOffset> Slots(DateOnly date, int intervalMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(intervalMinutes, 1);
        var slots = new List<DateTimeOffset>();
        var end = At(date, LastSlot);
        for (var slot = At(date, FirstSlot); slot <= end; slot = slot.AddMinutes(intervalMinutes))
        {
            slots.Add(slot);
        }

        return slots;
    }

    /// <summary>
    /// The expiries an index is asked for: from the list (ascending, today counts), the earliest <c>Weekly</c> dates and then the earliest <c>Monthly</c> month-end dates (the last date of each calendar month in the list) that are not among them,
    /// ascending. Nifty with 3 weekly and 1 monthly therefore asks 4 dates even when the nearest month-end is also a weekly one. A month whose last listed date is not yet known (the list ends inside it) still counts its last listed date.
    /// </summary>
    public static IReadOnlyList<DateOnly> IndexExpiries(IReadOnlyList<DateOnly> list, DateOnly today, ChainExpiryRule rule)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(rule);
        var future = list.Where(d => d >= today).Distinct().Order().ToList();
        var weekly = future.Take(Math.Max(0, rule.Weekly)).ToList();
        var monthly = future.GroupBy(d => (d.Year, d.Month)).Select(g => g.Max()).Where(d => !weekly.Contains(d)).Order().Take(Math.Max(0, rule.Monthly));
        return [.. weekly.Concat(monthly).Order()];
    }

    /// <summary>The first <paramref name="count"/> expiries of a stock that are not in the past.</summary>
    public static IReadOnlyList<DateOnly> StockExpiries(IReadOnlyList<DateOnly> list, DateOnly today, int count)
    {
        ArgumentNullException.ThrowIfNull(list);
        return [.. list.Where(d => d >= today).Distinct().Order().Take(Math.Max(0, count))];
    }
}
