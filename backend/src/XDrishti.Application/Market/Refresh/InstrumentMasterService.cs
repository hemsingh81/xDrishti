using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.Refresh;

/// <summary>
/// The refresh of the instrument master list as a service (<see cref="Id"/>, ADR 0010, ADR 0018): one run starts a refresh through the same command as the endpoint (trigger <c>daily</c> for a
/// scheduled run, <c>manual</c> for Run now) and waits for it; an unchanged file costs one conditional request. When a refresh is running already the run is skipped. <b>When</b> it runs is the scheduler's: by default every
/// day at 08:45; when the application starts and the latest scheduled time passed without a run since, however long ago, it refreshes at once (ADR 0010).
/// </summary>
public sealed class InstrumentMasterService(IServiceScopeFactory scopes, IInstrumentRefreshSettings settings, TimeProvider clock) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "instrument-master";

    /// <summary>Recorded as the requester of automatic runs.</summary>
    public const string Requester = "system:daily-refresh";

    public const string AnotherRefreshRunning = "A refresh of the instrument list was running already.";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Instrument master refresh",
        "Downloads Dhan's instrument list and NSE's sector list and updates the master list of instruments.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(8, 45), false, Weekdays.EveryDay), 0, 30, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { CatchUpReach = CatchUpReach.Latest });

    ServiceDefinition IScheduledService.Definition => Definition;

    /// <summary><c>Instruments:AutoRefresh:Enabled</c> and <c>Instruments:AutoRefresh:TimeIst</c>: read once, when the service has no stored row (ADR 0018 §3).</summary>
    public ServiceSettings? FirstStartSettings() =>
        Definition.Defaults with { Enabled = settings.AutoRefreshEnabled, Schedule = Schedule.Daily(settings.AutoRefreshTimeIst, false, Weekdays.EveryDay) };

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Guid runId;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var automatic = context.Trigger != ServiceRunTrigger.Manual;
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<StartRefreshCommand, RefreshStarted>>();
            var started = await handler.HandleAsync(new StartRefreshCommand(automatic ? RefreshTrigger.Daily : RefreshTrigger.Manual, false, automatic ? Requester : context.RequestedBy), cancellationToken);
            if (started.IsFailure)
            {
                return ServiceOutcome.Skipped(started.Error.Code == MarketErrors.RefreshRunningCode ? AnotherRefreshRunning : started.Error.Message);
            }

            runId = started.Value.RefreshId;
            context.MarkHandedOff(); // the refresh goes on in the refresh worker even if this run is cancelled
        }

        var wait = JobPolling.FirstWait;
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var run = await scope.ServiceProvider.GetRequiredService<IRefreshRunStore>().GetAsync(runId, cancellationToken);
            if (run is null)
            {
                return ServiceOutcome.Failed("The refresh record is gone.");
            }

            if (!run.IsRunning)
            {
                return run.Status switch
                {
                    RefreshStatus.Failed => ServiceOutcome.Failed(run.Error ?? "The refresh failed."),
                    RefreshStatus.Unchanged => ServiceOutcome.Succeeded("The source files had not changed."),
                    _ => ServiceOutcome.Succeeded(),
                };
            }

            await Task.Delay(wait, clock, cancellationToken);
            wait = JobPolling.Next(wait);
        }
    }
}
