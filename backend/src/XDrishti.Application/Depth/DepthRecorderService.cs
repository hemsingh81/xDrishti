using System.Text.Json;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Depth;

/// <summary>
/// The depth recorder as a service of the Services screen (<see cref="Id"/>, P7-16): a <b>window</b> service, 09:14-15:31 India time on trading days, run by the <c>xd-feed</c> host (<see cref="IExternallyRunService"/>: shown and edited here, never
/// started by a scheduler). It <b>starts paused</b> (the definition's defaults are disabled), one of the three gates; the others are the deployment flag <c>Depth:Enabled</c> and the owner's stored switch. Retries are 0 (the recorder is its own
/// reconnect loop), no catch-up, the owner is told when a run fails. <see cref="RunAsync"/> is never called by a scheduler.
/// </summary>
public sealed class DepthRecorderService : IScheduledService, IExternallyRunService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "depth-recorder";

    /// <summary>The host that runs it.</summary>
    public const string RunsAs = "xd-feed";

    public const string WindowEnded = "Window ended";

    public const string Paused = "Paused";

    public const string Stopped = "Stopped";

    public static readonly TimeOnly WindowStart = new(9, 14);

    public static readonly TimeOnly WindowEnd = new(15, 31);

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Depth recorder",
        "Records the 20-level market depth of up to 50 NSE shares about once a second during the session, compressed, for the research of a later phase. Off until you switch it on (deployment flag, your password, and this service). Nothing reads the data and nothing trades on it.",
        RunsAs,
        [ScheduleType.Window],
        new ServiceSettings(false, Schedule.Window(WindowStart, WindowEnd), 0, ServiceSettings.MaxTimeoutMinutes, false, true, new Dictionary<string, JsonElement>()),
        [],
        new ServiceConstraints());

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ServiceOutcome.Skipped("The depth recorder is started by the xd-feed service, not by this scheduler."));
}
