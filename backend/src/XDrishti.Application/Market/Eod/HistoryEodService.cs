using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.Eod;

/// <summary>
/// The daily-file history job (<see cref="Id"/>, CF-04 Part A): after the end-of-day run it loads the NSE bhavcopy and delivery files of the days not yet in the ledger, one file per day, throttled, resumable. It starts <b>paused</b> and with
/// <c>Data:History:Nse:Enabled</c> off (the default) a run makes no HTTP call at all and ends as skipped. It touches no broker, no account, no order and no trading configuration.
/// </summary>
public sealed class HistoryEodService(IServiceScopeFactory scopes, IEodSettings settings) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "history-eod";

    public const string DisabledReason = "disabled: Data:History:Nse:Enabled is off";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Daily history files",
        "Loads NSE's end-of-day price and delivery files, one file per trading day, newest first, into the daily table. Off until the owner has read the NSE terms, added the endpoint to the allow-list and switched Data:History:Nse:Enabled on; with the switch off a run makes no request. No broker, no order.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(18, 30)), 0, 240, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { CatchUpReach = CatchUpReach.SameDay });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!settings.Enabled)
        {
            return ServiceOutcome.Skipped(DisabledReason);
        }

        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<LoadEodCommand, EodLoadSummary>>().HandleAsync(new LoadEodCommand(), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Code + ": " + result.Error.Message);
        }

        return result.Value.Stopped is "fetch_failed" ? ServiceOutcome.Failed(result.Value.Text) : ServiceOutcome.Succeeded(result.Value.Text);
    }
}
