using System.Text.Json;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Live;

/// <summary>
/// The live feed as a service of the Services screen (<see cref="Id"/>, ADR 0024 §1, ADR 0018): a <b>window</b> service, up 09:00-15:35 India time on trading days, run by the <c>xd-feed</c> host - not by the scheduler of <c>xd-api</c>, which
/// cannot keep a 6 h 35 min run up (a run is capped at 240 minutes) and does not own the connection. The definition is here so that the API can show and edit its schedule (<see cref="IExternallyRunService"/>: registered for
/// display by <c>AddApplication</c> in every host, started by none of the schedulers); the feed's own
/// <see cref="LiveFeedSupervisor"/> reads the same row, opens one run record per window and closes it with the reason. Retries are 0 (the connection manager is its own retry) and the timeout is the largest the rows allow, which the supervisor ignores.
/// <see cref="RunAsync"/> is never called by a scheduler.
/// </summary>
public sealed class LiveFeedService : IScheduledService, IExternallyRunService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "live-feed";

    /// <summary>The host that runs it.</summary>
    public const string RunsAs = "xd-feed";

    /// <summary>The run ended because its window did (or a manual run reached its end).</summary>
    public const string WindowEnded = "Window ended";

    public const string Paused = "Paused";

    public const string TokenRefused = "Token refused";

    public const string Flapping = "Flapping";

    /// <summary>The feed process was told to stop (a deploy, a restart).</summary>
    public const string Stopped = "Stopped";

    /// <summary>The message of the skipped run recorded when the window opens and the Data account has no usable token.</summary>
    public const string NoUsableToken = "No usable token";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Live feed",
        "Streams the prices of the open holdings, the alerts and a few indices from Dhan's market feed during the session, so that values, P&L and price alerts are seconds old.",
        RunsAs,
        [ScheduleType.Window],
        new ServiceSettings(true, Schedule.Window(new TimeOnly(9, 0), new TimeOnly(15, 35)), 0, ServiceSettings.MaxTimeoutMinutes, false, true, new Dictionary<string, JsonElement>()),
        [],
        new ServiceConstraints { MustCover = TimeRange.MarketHours });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ServiceOutcome.Skipped("The live feed is started by the xd-feed service, not by this scheduler."));
}
