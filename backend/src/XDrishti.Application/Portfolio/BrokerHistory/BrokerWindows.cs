namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// Cuts the dates a broker history run has to read into windows (ADR 0025). Dhan's real limit on the span of one request is not known yet, so windows start at <see cref="MaxDays"/> days (a constant, tuned from what Dhan returns); a window Dhan
/// refuses as too large is halved once. Pure.
/// </summary>
internal static class BrokerWindows
{
    /// <summary>The longest window, in dates.</summary>
    public const int MaxDays = 30;

    /// <summary>The windows from <paramref name="from"/> to <paramref name="to"/> (inclusive), oldest first, each at most <paramref name="maxDays"/> dates, without gaps or overlaps. Nothing when <paramref name="from"/> is after <paramref name="to"/>.</summary>
    public static IReadOnlyList<DateWindow> Split(DateOnly from, DateOnly to, int maxDays = MaxDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDays, 1);
        var windows = new List<DateWindow>();
        for (var start = from; start <= to; start = start.AddDays(maxDays))
        {
            var end = start.AddDays(maxDays - 1);
            windows.Add(new DateWindow(start, end < to ? end : to));
        }

        return windows;
    }

    /// <summary>The two halves of a window (the first gets the extra date of an odd span); null when the window is a single date and cannot be halved.</summary>
    public static (DateWindow First, DateWindow Second)? Halve(DateWindow window)
    {
        if (window.Days < 2)
        {
            return null;
        }

        var firstDays = (window.Days + 1) / 2;
        var middle = window.From.AddDays(firstDays - 1);
        return (new DateWindow(window.From, middle), new DateWindow(middle.AddDays(1), window.To));
    }
}
