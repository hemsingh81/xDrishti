using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// The system baskets' refresh as a service (<see cref="Id"/>, spec BK-01): it looks every 5 minutes from 07:30 to 23:00 IST and carries out <see cref="RunBasketFetchCommand"/>, which asks each enabled index list once a day from the run time (twice in a review window) and, after a failure, again after 5, 30 and 120 minutes (<c>Baskets:System:Fetch:BackoffMinutes</c>). It starts <b>paused</b>; while
/// <c>Baskets:System:Fetch:Enabled</c> is off a run makes no request and ends as skipped (disabled). The retries are the sources' own (not the scheduler's); it cannot place an order.
/// </summary>
public sealed class SystemBasketsService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "system-baskets";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "System baskets",
        "From 07:30 reads the official NSE index lists (one conditional request per enabled file) and keeps the index baskets' membership with dates. Fails closed: a file that looks wrong is rejected and the last good list stays. Off by default; it never places an order.",
        "xd-api",
        [ScheduleType.Interval, ScheduleType.Daily],
        new ServiceSettings(false, Schedule.Interval(5, new TimeOnly(7, 30), new TimeOnly(23, 0), false, Weekdays.EveryDay), 0, 10, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { RecoveryClass = RecoveryClass.LatestOnly, CatchUpReach = CatchUpReach.SameDay });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunBasketFetchCommand, BasketFetchRun>>();
        var result = await handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:system-baskets"), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.Code == BasketErrors.FetchDisabledCode ? ServiceOutcome.Skipped("disabled") : result.Error.Code == BasketErrors.FetchRunningCode ? ServiceOutcome.Skipped("already running") : ServiceOutcome.Failed(result.Error.Message);
        }

        return result.Value.Outcomes.Any(o => o.Outcome == FetchOutcomes.Failed) ? ServiceOutcome.Failed(result.Value.Summary) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
