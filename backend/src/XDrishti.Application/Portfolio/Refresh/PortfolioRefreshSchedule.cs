using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Portfolio.Refresh;

/// <summary>
/// When the automatic holdings refresh of an account is due (ADR 0012, 0013). Pure: the caller passes the clock and what happened so far. The refresh is
/// <b>daily</b>: once a day, from the configured India time on; when the time was missed (the application was down) the run is made as soon as possible
/// the same day. A failed or skipped automatic attempt counts as the day's run. Off is never due.
/// </summary>
public static class PortfolioRefreshSchedule
{
    public static bool IsDue(DateTimeOffset now, PortfolioRefresh refresh, SyncActivity activity)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(activity);
        return refresh.Mode == PortfolioRefreshMode.Daily && now >= ScheduledToday(now, refresh) && !Attempted(activity, ScheduledToday(now, refresh));
    }

    /// <summary>When the next run happens: now when one is due, null when the refresh is off.</summary>
    public static DateTimeOffset? NextRun(DateTimeOffset now, PortfolioRefresh refresh, SyncActivity activity)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(activity);
        if (refresh.Mode != PortfolioRefreshMode.Daily)
        {
            return null;
        }

        var scheduled = ScheduledToday(now, refresh);
        if (now < scheduled)
        {
            return scheduled;
        }

        return Attempted(activity, scheduled) ? At(IndiaTime.Today(now).AddDays(1), refresh.DailyTimeIst) : now;
    }

    private static bool Attempted(SyncActivity activity, DateTimeOffset scheduled) => activity.LastAutoAttemptAt is { } attempt && attempt >= scheduled;

    private static DateTimeOffset ScheduledToday(DateTimeOffset now, PortfolioRefresh refresh) => At(IndiaTime.Today(now), refresh.DailyTimeIst);

    private static DateTimeOffset At(DateOnly date, TimeOnly timeIst) => new(date.ToDateTime(timeIst, DateTimeKind.Unspecified), IndiaTime.Offset);
}
