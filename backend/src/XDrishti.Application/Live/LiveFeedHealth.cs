using System.Globalization;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Live;

/// <summary>The status word of the Services screen for the live feed and what its heartbeat says.</summary>
internal readonly record struct LiveFeedReading(string Status, ServiceLiveView Live);

/// <summary>
/// The live feed's row on the Services screen (P2-10 criterion 15) - pure. The feed is run by <c>xd-feed</c>, so its status cannot come from this process's runner: it comes from the stored row, the latest run and the feed's heartbeat.
/// <list type="bullet">
/// <item><b>failed</b> - it should be up (inside its window on a trading day, or a manual run is in force) or has a run open, and the heartbeat is missing or older than 60 seconds (<see cref="NotRunning"/>); or its latest run failed (token
/// refused, flapping, interrupted);</item>
/// <item><b>running</b> - a run is open and the feed reports;</item>
/// <item><b>paused</b> - the owner paused it;</item>
/// <item><b>healthy</b> - otherwise, also outside the window with the feed idle (<c>closed</c>) or not reporting, and in the window while it has no token (the run is recorded as skipped).</item>
/// </list>
/// A feed that stopped on purpose (its last beat says <see cref="FeedStates.Stopped"/>) is not reporting.
/// </summary>
internal static class LiveFeedHealth
{
    public const string NotRunning = "The live feed is not running.";

    /// <summary>The state word given when the feed reports nothing (or has stopped).</summary>
    public const string NotReporting = "not_running";

    public static LiveFeedReading Evaluate(EffectiveService service, ServiceRun? lastRun, bool runOpen, TradingCalendar calendar, DateTimeOffset now, FeedStatus? heartbeat, DateTimeOffset? manualUntil)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(calendar);
        var shouldBeUp = FeedWindow.Decide(service.Settings, calendar, now, manualUntil).ShouldBeUp;
        var reporting = heartbeat is { IsAlive: true } && heartbeat.State != FeedStates.Stopped;
        var status = !reporting && (shouldBeUp || runOpen) ? "failed"
            : runOpen ? "running"
            : !service.Settings.Enabled ? "paused"
            : lastRun is { Status: ServiceRunStatus.Failed } ? "failed"
            : "healthy";
        return new LiveFeedReading(status, Describe(heartbeat, reporting, shouldBeUp || runOpen));
    }

    private static ServiceLiveView Describe(FeedStatus? heartbeat, bool reporting, bool wanted)
    {
        if (!reporting)
        {
            var summary = wanted ? NotRunning : heartbeat?.State == FeedStates.Stopped ? "Stopped." : "Idle: outside the window.";
            return new ServiceLiveView(NotReporting, false, 0, heartbeat?.Details?.LastTickAt, 0, heartbeat?.LastSeenAt, summary);
        }

        var details = heartbeat!.Details;
        var (connected, subscribed, lastTick, rate) = (details?.Connected ?? false, details?.Subscribed ?? 0, details?.LastTickAt, details?.TicksPerSecond ?? 0);
        var text = heartbeat.State switch
        {
            FeedStates.Connected => Streaming(subscribed, lastTick, rate),
            FeedStates.Idle => "Connected · nothing to follow.",
            FeedStates.Connecting => "Connecting.",
            FeedStates.Reconnecting => "Reconnecting.",
            FeedStates.Paused => "Paused.",
            FeedStates.NoToken => "No usable token: connect the Data account.",
            FeedStates.TokenRefused => "The Data account's token was refused.",
            FeedStates.Flapping => "Reconnecting too often; waiting 5 minutes.",
            FeedStates.Closed => wanted ? "Starting." : "Idle: outside the window.",
            _ => heartbeat.State,
        };
        return new ServiceLiveView(heartbeat.State, connected, subscribed, lastTick, rate, heartbeat.LastSeenAt, text);
    }

    private static string Streaming(int subscribed, DateTimeOffset? lastTick, double rate)
    {
        var last = lastTick is { } at ? $"last tick {IndiaTime.ToIst(at).ToString("HH:mm:ss", CultureInfo.InvariantCulture)} IST" : "no tick yet";
        return $"Connected · {subscribed} instruments · {last} · {rate.ToString("0.#", CultureInfo.InvariantCulture)} ticks/s";
    }
}
