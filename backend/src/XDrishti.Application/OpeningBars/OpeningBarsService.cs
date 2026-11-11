using System.Text.Json;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.OpeningBars;

/// <summary>
/// The opening-minute recorder as a service of the Services screen (<see cref="Id"/>, BK-03b): a <b>window</b> service, 09:10-09:32 India time on trading days, run by the <c>xd-feed</c> host (<see cref="IExternallyRunService"/>: shown and edited here, never started
/// by a scheduler). It <b>starts paused</b>, one of the three gates; the others are the deployment flag <c>OpeningBars:Enabled</c> and the owner's stored switch <c>baskets.opening_bars</c>. The message of its last run carries the day's counts.
/// </summary>
public sealed class OpeningBarsService : IScheduledService, IExternallyRunService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "opening-bars";

    /// <summary>The host that runs it.</summary>
    public const string RunsAs = "xd-feed";

    public static readonly TimeOnly WindowStart = new(9, 10);

    public static readonly TimeOnly WindowEnd = new(9, 32);

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Opening-minute bars",
        "Records the 1-minute bars of 09:15 to 09:31 from the live feed (a second connection, quote mode) so that the Gappers (09:20) and Opening-range wide (09:30) baskets can be formed once, from complete minutes, and frozen. Off until you switch it on (deployment flag, your password, and this service). Read-only market data: no order, no account call.",
        RunsAs,
        [ScheduleType.Window],
        new ServiceSettings(false, Schedule.Window(WindowStart, WindowEnd), 0, ServiceSettings.MaxTimeoutMinutes, false, true, new Dictionary<string, JsonElement>()),
        [],
        new ServiceConstraints { RecoveryClass = RecoveryClass.TimeBound });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ServiceOutcome.Skipped("The opening-minute recorder is started by the xd-feed service, not by this scheduler."));
}
