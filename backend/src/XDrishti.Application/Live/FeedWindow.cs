using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Live;

/// <summary>Whether the feed should be up now and, when it should not, which state word says why.</summary>
internal readonly record struct FeedWindowDecision(bool ShouldBeUp, bool Manual, string DownState);

/// <summary>
/// The window rule of the feed (ADR 0024 §1) - pure. The feed is wanted up when a manual run is in force (the marker of "Run now" is in the future), or when the service is enabled and now is inside a band of its schedule on a day the
/// schedule fires on (the window 09:00-15:35 on trading days; the calendar decides which days). A paused service is not up unless the owner asked for a manual run. The end of the band is exclusive.
/// </summary>
internal static class FeedWindow
{
    public static FeedWindowDecision Decide(ServiceSettings settings, TradingCalendar calendar, DateTimeOffset now, DateTimeOffset? manualUntil)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(calendar);
        if (manualUntil is { } until && until > now)
        {
            return new FeedWindowDecision(true, true, FeedStates.Closed);
        }

        if (!settings.Enabled)
        {
            return new FeedWindowDecision(false, false, FeedStates.Paused);
        }

        var inside = ScheduleCalculator.BandsOn(settings.Schedule, IndiaTime.Today(now), calendar).Any(band => now >= band.From && now < band.To);
        return new FeedWindowDecision(inside, false, FeedStates.Closed);
    }
}
