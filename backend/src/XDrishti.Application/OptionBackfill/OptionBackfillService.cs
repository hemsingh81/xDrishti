using System.Text.Json;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.OptionBackfill;

/// <summary>
/// The expired-options backfill as a service of the Services screen (<see cref="Id"/>, P14-03): run by the <c>xd-feed</c> host (<see cref="IExternallyRunService"/>: shown and edited here, never started by a scheduler; "Run now" is refused). It
/// <b>starts paused</b>, one of the four gates; the others are the deployment flag <c>Options:Backfill:Enabled</c>, the owner's stored switch and the owner's acceptance of the probe result. The code itself runs it only between 16:30 and 08:30 India time,
/// every day, so its schedule is "continuous". Retries 0, no catch-up, the owner is told when a run fails.
/// </summary>
public sealed class OptionBackfillService : IScheduledService, IExternallyRunService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "options-backfill";

    /// <summary>The host that runs it: it holds the vault key the Data account's token needs.</summary>
    public const string RunsAs = "xd-feed";

    public const string Paused = "Paused";

    public const string Stopped = "Stopped";

    public const string WindowEnded = "Night window ended";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Expired-options backfill",
        "Fetches up to five years of expired-option bars from Dhan for the chosen indices and shares, one request a second, only between 16:30 and 08:30, and stores them compressed for the research of a later phase. Off until you accept the probe result and switch it on (deployment flag, your password, and this service). Nothing reads the data and nothing trades on it.",
        RunsAs,
        [ScheduleType.Continuous],
        new ServiceSettings(false, Schedule.Continuous(), 0, ServiceSettings.MaxTimeoutMinutes, false, true, new Dictionary<string, JsonElement>()),
        [],
        new ServiceConstraints());

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ServiceOutcome.Skipped("The expired-options backfill is started by the xd-feed service, not by this scheduler."));
}
