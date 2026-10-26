namespace XDrishti.Domain.Market.Context;

/// <summary>
/// Pure: the share of the tracked shares that closed above their simple average (P3-01e, ADR 0033 decision 4). A share votes "above" when its close on the day is strictly greater than the mean of its closes on the
/// <see cref="BreadthOptions.SmaSessions"/> sessions of the calendar that end on the day (the day included); it votes only with a complete bar on every one of those sessions, raw closes, and no ex-date of a corporate
/// action in the window. Nothing after the day is read, so the figure cannot look ahead. A thin sample (fewer voters than the minimum, or voters under the minimum share of the shares that have a bar) is not a number.
/// </summary>
public static class BreadthCalculator
{
    public static BreadthResult Compute(
        DateOnly date,
        IReadOnlyList<DateOnly> sessions,
        IReadOnlyList<BreadthInput> shares,
        IReadOnlyDictionary<Guid, IReadOnlySet<DateOnly>> exDatesByShare,
        BreadthOptions options)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(exDatesByShare);
        ArgumentNullException.ThrowIfNull(options);

        var end = BinarySearch(sessions, date);
        var window = end >= options.SmaSessions - 1 ? Slice(sessions, end - options.SmaSessions + 1, options.SmaSessions) : null;
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var above = 0;
        var voters = 0;
        var withBar = 0;
        foreach (var share in shares)
        {
            if (end < 0 || !share.Closes.TryGetValue(date, out var close))
            {
                Count(reasons, BreadthResult.NoBar);
                continue;
            }

            withBar++;
            if (window is null || !Complete(share, window))
            {
                Count(reasons, BreadthResult.Warmup);
                continue;
            }

            var sum = 0.0;
            var bad = false;
            foreach (var session in window)
            {
                var value = share.Closes[session];
                if (!double.IsFinite(value) || value <= 0)
                {
                    bad = true;
                    break;
                }

                sum += value;
            }

            if (bad)
            {
                Count(reasons, BreadthResult.BadBar);
                continue;
            }

            if (exDatesByShare.TryGetValue(share.ShareId, out var exDates) && exDates.Any(d => d >= window[0] && d <= date))
            {
                Count(reasons, BreadthResult.ActionInWindow);
                continue;
            }

            voters++;
            if (close > sum / options.SmaSessions)
            {
                above++;
            }
        }

        var enough = voters > 0 && withBar >= options.MinBarShare * shares.Count && voters >= options.MinVoters && voters >= options.MinShare * withBar;
        return new BreadthResult(
            enough ? 100.0 * above / voters : null,
            voters,
            shares.Count,
            shares.Count - voters,
            reasons,
            enough ? BreadthResult.ValueState : BreadthResult.Insufficient);
    }

    private static void Count(SortedDictionary<string, int> reasons, string reason) => reasons[reason] = reasons.GetValueOrDefault(reason) + 1;

    private static bool Complete(BreadthInput share, DateOnly[] window)
    {
        foreach (var session in window)
        {
            if (!share.Closes.ContainsKey(session))
            {
                return false;
            }
        }

        return true;
    }

    private static DateOnly[] Slice(IReadOnlyList<DateOnly> sessions, int start, int count)
    {
        var window = new DateOnly[count];
        for (var i = 0; i < count; i++)
        {
            window[i] = sessions[start + i];
        }

        return window;
    }

    /// <summary>The index of the date in the ascending sessions, or -1 when it is not a session.</summary>
    private static int BinarySearch(IReadOnlyList<DateOnly> sessions, DateOnly date)
    {
        int low = 0, high = sessions.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) >>> 1;
            var compared = sessions[mid].CompareTo(date);
            if (compared == 0)
            {
                return mid;
            }

            if (compared < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return -1;
    }
}
