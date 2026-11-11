using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Application.OpeningBars;

/// <summary>
/// The pure rules of BK-03b: which bars the engine may see (never one at or after the formation time), when a window is complete, why a whole basket is not formed (a missing input is never a pass) and which shares are subscribed when the universe is over the cap.
/// </summary>
internal static class OpeningWindows
{
    public const string NoLiveBars = "no_live_bars";

    public const string FeedGap = "feed_gap";

    public const string LowDisk = "low_disk";

    public const string Late = "late";

    private static readonly TimeSpan Ist = new(5, 30, 0);

    public static DateTimeOffset At(DateOnly day, TimeOnly time) => new(day.ToDateTime(time), Ist);

    /// <summary>The bars handed to the engine: those whose minute starts before the formation time. The bar of the formation minute itself is never included, even when written.</summary>
    public static IEnumerable<MinuteBar> Before(IEnumerable<MinuteBar> bars, DateTimeOffset formation) => bars.Where(b => b.MinuteStart < formation);

    /// <summary>Whether every minute from the open to the formation time has a bar. A minute with no bar is missing, never filled forward.</summary>
    public static bool IsComplete(IEnumerable<MinuteBar> bars, DateOnly day, DateTimeOffset formation)
    {
        var open = At(day, OpeningBarsSettings.Open);
        var needed = (int)(formation - open).TotalMinutes;
        return bars.Where(b => b.MinuteStart >= open && b.MinuteStart < formation).Select(b => b.MinuteStart).Distinct().Count() == needed;
    }

    /// <summary>
    /// Why no version of the day can be formed at <paramref name="formation"/>, or null when the connection's record allows one. A low-disk stop before the formation: <c>low_disk</c>; no recorder start: <c>no_live_bars</c>; a start after the open, a second start
    /// (a restart leaves a hole) or any gap overlapping [open, formation): <c>feed_gap</c>.
    /// </summary>
    public static string? WholeReason(IReadOnlyList<OpeningBarsEvent> events, DateOnly day, DateTimeOffset formation)
    {
        var open = At(day, OpeningBarsSettings.Open);
        if (events.Any(e => e.Kind == OpeningBarsEvent.LowDisk && e.From < formation))
        {
            return LowDisk;
        }

        var starts = events.Where(e => e.Kind == OpeningBarsEvent.Started && e.From < formation).OrderBy(e => e.From).ToList();
        if (starts.Count == 0)
        {
            return NoLiveBars;
        }

        if (starts.Count > 1 || starts[0].From > open || events.Any(e => e.Kind == OpeningBarsEvent.Gap && e.From < formation && (e.To ?? DateTimeOffset.MaxValue) > open))
        {
            return FeedGap;
        }

        return null;
    }

    /// <summary>
    /// The shares the recorder subscribes: at most <paramref name="cap"/>, the most liquid by ADV20 (the mean traded value of the last 20 daily bars) first, ties by symbol; the rest are returned as cut (the version names them <c>not_subscribed</c>).
    /// The same function the recorder and the freeze use, so both agree on the set.
    /// </summary>
    public static (IReadOnlyList<UniverseInstrument> Kept, IReadOnlyList<UniverseInstrument> Cut) Subscribed(IReadOnlyList<UniverseInstrument> universe, int cap)
    {
        var ordered = universe.OrderByDescending(Adv20).ThenBy(u => u.Bars.Symbol, StringComparer.Ordinal).ToList();
        return (ordered.Take(cap).ToList(), ordered.Skip(cap).ToList());
    }

    private static double Adv20(UniverseInstrument instrument)
    {
        var last = instrument.Bars.Daily.TakeLast(20).ToList();
        return last.Count == 0 ? 0 : last.Average(b => b.Close * b.Volume);
    }
}
