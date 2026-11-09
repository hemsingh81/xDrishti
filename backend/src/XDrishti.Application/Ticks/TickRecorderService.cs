using System.Text.Json;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Ticks;

/// <summary>
/// The trade-tick recorder as a service of the Services screen (<see cref="Id"/>, P7-16b): a <b>window</b> service, 09:14-15:31 India time on trading days, run by the <c>xd-feed</c> host (<see cref="IExternallyRunService"/>: shown and edited here, never
/// started by a scheduler). It <b>starts paused</b>, one of the three gates; the others are the deployment flag <c>Ticks:Enabled</c> and the owner's stored switch. Retries 0, no catch-up, the owner is told when a run fails.
/// </summary>
public sealed class TickRecorderService : IScheduledService, IExternallyRunService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "tick-recorder";

    /// <summary>The host that runs it.</summary>
    public const string RunsAs = "xd-feed";

    public const string WindowEnded = "Window ended";

    public const string Paused = "Paused";

    public const string Stopped = "Stopped";

    public static readonly TimeOnly WindowStart = new(9, 14);

    public static readonly TimeOnly WindowEnd = new(15, 31);

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Trade-tick recorder",
        "Records the last-trade updates (price, quantity, cumulative volume) of the day's recorded shares whenever the volume rises, compressed, for the research of a later phase. Off until you switch it on (deployment flag, your password, and this service). Nothing reads the data and nothing trades on it.",
        RunsAs,
        [ScheduleType.Window],
        new ServiceSettings(false, Schedule.Window(WindowStart, WindowEnd), 0, ServiceSettings.MaxTimeoutMinutes, false, true, new Dictionary<string, JsonElement>()),
        [],
        new ServiceConstraints { RecoveryClass = RecoveryClass.TimeBound });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ServiceOutcome.Skipped("The trade-tick recorder is started by the xd-feed service, not by this scheduler."));
}
