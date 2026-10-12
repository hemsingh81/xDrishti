using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Market.Bars.Dhan;

/// <summary>
/// Decides what the Dhan phase asks for (ADR 0016 §2): from two days before the instrument's last stored bar (the overlap reconciles Dhan's bars with the
/// stored ones), or <c>Data:Dhan:InitialDays</c> back when it has none, up to now, in windows of at most 90 days (Dhan's limit per request).
/// Independent of HTTP, so every <see cref="IIntradayBarSource"/> asks for the same things.
/// </summary>
public static class DhanFetchPlanner
{
    public const int MaxWindowDays = 90;

    public const int OverlapDays = 2;

    /// <summary>The windows for one instrument, oldest first; neighbours share their boundary instant.</summary>
    public static IReadOnlyList<FetchWindow> Windows(DateTimeOffset? lastBar, DateTimeOffset now, int initialDays)
    {
        var from = lastBar is { } last ? last.AddDays(-OverlapDays) : now.AddDays(-Math.Max(initialDays, 1));
        if (from >= now)
        {
            from = now.AddDays(-OverlapDays);
        }

        var windows = new List<FetchWindow>();
        var cursor = from;
        while (cursor < now)
        {
            var to = cursor.AddDays(MaxWindowDays);
            if (to > now)
            {
                to = now;
            }

            windows.Add(new FetchWindow(cursor, to));
            cursor = to;
        }

        return windows;
    }

    public static IntradayFetchPlan Plan(IReadOnlyList<FetchTarget> targets, DateTimeOffset now, int initialDays)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return new IntradayFetchPlan([.. targets.Select(t => new IntradayTarget(t.InstrumentId, t.Symbol, t.Type, t.SecurityId, t.Segment, Windows(t.LastBar, now, initialDays)))]);
    }
}
