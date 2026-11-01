using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.OptionQuality;

/// <summary>
/// The quality checks of the option archive as a service (<see cref="Id"/>, P14-04, ADR 0178): every trading day at 16:15 India time, in the scheduler of <c>xd-api</c>, one run checks what no run has checked yet (see <see cref="OptionArchiveQualityRunner"/>). It is
/// <b>on by default</b> because it only reads the archive and writes its own result rows; it calls no broker. Rolling windows the backfill finished at night are picked up by the next run, so there is no coupling to the backfill. Retries 1, catch-up of the same day.
/// </summary>
public sealed class OptionArchiveQualityService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "options-quality";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Option archive quality",
        "After the session, checks the recorded option chains and the backfilled rolling bars: coverage, put-call parity, IV and Greek sanity, open-interest and volume jumps, rolling-series stitching. Reads the archive, writes only its own result rows, calls no broker, trades on nothing.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(16, 15)), 1, 60, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { CatchUpReach = CatchUpReach.SameDay });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<OptionArchiveQualityRunner>();
        var summary = await runner.RunAsync(context.Trigger == ServiceRunTrigger.Manual ? "manual" : "daily", cancellationToken);
        return ServiceOutcome.Succeeded(summary.Text);
    }
}
