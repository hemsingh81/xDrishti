using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Application.OptionBackfill;

/// <summary>
/// The fetch keys of the backfill, pure (P14-03 criterion 2), in a fixed order: underlying, expiry flag and code (the series order below), offset (low to high), side (<c>CE</c> then <c>PE</c>), window (the newest first).
/// <list type="bullet">
/// <item>NIFTY at 5 minutes: <c>WEEK</c> 1, <c>WEEK</c> 2, <c>MONTH</c> 1. BANKNIFTY at 5 minutes: <c>MONTH</c> 1, <c>MONTH</c> 2, and <c>WEEK</c> 1 for windows that end on or before 2024-11-20 (its weekly expiry was withdrawn then). Offsets -<c>IndexOffsets</c> to +<c>IndexOffsets</c>. No other index.</item>
/// <item>The first <c>StockCount</c> shares of the targets (they come ranked by turnover) at 15 minutes: <c>MONTH</c> 1, offsets -<c>StockOffsets</c> to +<c>StockOffsets</c>.</item>
/// <item>Windows of 30 days on a fixed grid (<see cref="Epoch"/> + 30 k days), the newest ending on or before the first day of the current month, for <c>Years</c> years back: 60 or 61 windows for 5 years (61 on 2026-11-01). The oldest window's request starts no earlier than five years back.</item>
/// </list>
/// The expiry codes and the offset limits are UNVERIFIED until the owner's probe result is recorded (ADR 0177).
/// </summary>
public static class BackfillPlanner
{
    public const string Nifty = "NSE:NIFTY";

    public const string BankNifty = "NSE:BANKNIFTY";

    public const int IndexInterval = 5;

    public const int StockInterval = 15;

    /// <summary>BANKNIFTY weekly windows that end after this day are not planned.</summary>
    public static readonly DateOnly BankNiftyWeeklyEnd = new(2024, 11, 20);

    private static readonly (char Flag, int Code, bool WeeklyCut)[] NiftySeries = [('W', 1, false), ('W', 2, false), ('M', 1, false)];

    private static readonly (char Flag, int Code, bool WeeklyCut)[] BankNiftySeries = [('M', 1, false), ('M', 2, false), ('W', 1, true)];

    private static readonly (char Flag, int Code, bool WeeklyCut)[] StockSeries = [('M', 1, false)];

    /// <summary>The day the window grid starts from. Every window is <c>Epoch + 30 k</c> days, whatever the date today is, so a new month only ADDS windows at the new end (and the oldest fall off): a key that is done stays done.</summary>
    public static readonly DateOnly Epoch = new(2020, 1, 1);

    /// <summary>The first day of every window, newest first: 30 days each on the fixed grid of <see cref="Epoch"/>, the newest ending on or before the first day of the month of <paramref name="today"/>, back to the first whose end is after <paramref name="years"/> years before that day (always 60 or 61 windows for 5 years: the count moves with the month).</summary>
    public static IReadOnlyList<DateOnly> Windows(DateOnly today, int years)
    {
        var end = new DateOnly(today.Year, today.Month, 1);
        var start = end.AddYears(-Math.Max(1, years));
        var newest = ((end.DayNumber - Epoch.DayNumber) / BackfillKey.WindowDays) - 1; // the last k with Epoch + 30 (k + 1) <= end
        var oldest = (start.DayNumber - Epoch.DayNumber) / BackfillKey.WindowDays; // the first k whose window ends after the start
        var windows = new List<DateOnly>(Math.Max(0, newest - oldest + 1));
        for (var k = newest; k >= oldest; k--)
        {
            windows.Add(Epoch.AddDays(BackfillKey.WindowDays * k));
        }

        return windows;
    }

    /// <summary>The earliest day Dhan can be asked for (five years back at most): the oldest window of the grid may begin a few days before it, and its request is clamped to it.</summary>
    public static DateOnly Earliest(DateOnly today, int years) => new DateOnly(today.Year, today.Month, 1).AddYears(-Math.Max(1, years));

    public static IEnumerable<BackfillItem> Plan(IReadOnlyList<BackfillTarget> targets, OptionBackfillSettings settings, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(settings);
        var windows = Windows(today, settings.Years);
        var earliest = Earliest(today, settings.Years);
        foreach (var index in new[] { (Nifty, NiftySeries), (BankNifty, BankNiftySeries) })
        {
            var target = targets.FirstOrDefault(t => t.IsIndex && string.Equals(t.Symbol, index.Item1, StringComparison.Ordinal));
            if (target is null)
            {
                continue;
            }

            foreach (var item in Series(target, index.Item2, "OPTIDX", IndexInterval, settings.IndexOffsets, windows, earliest))
            {
                yield return item;
            }
        }

        foreach (var stock in targets.Where(t => !t.IsIndex).Take(Math.Max(0, settings.StockCount)))
        {
            foreach (var item in Series(stock, StockSeries, "OPTSTK", StockInterval, settings.StockOffsets, windows, earliest))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<BackfillItem> Series(BackfillTarget target, (char Flag, int Code, bool WeeklyCut)[] series, string instrument, int interval, int offsets, IReadOnlyList<DateOnly> windows, DateOnly earliest)
    {
        foreach (var (flag, code, weeklyCut) in series)
        {
            for (var offset = -offsets; offset <= offsets; offset++)
            {
                foreach (var side in new[] { "CE", "PE" })
                {
                    foreach (var from in windows)
                    {
                        var key = new BackfillKey(target.InstrumentId, flag, code, offset, side, interval, from);
                        if (weeklyCut && key.To > BankNiftyWeeklyEnd)
                        {
                            continue;
                        }

                        yield return new BackfillItem(key, target.Scrip, instrument, target.StrikeStepPaise, earliest);
                    }
                }
            }
        }
    }
}
