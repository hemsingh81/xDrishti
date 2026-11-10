using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// The system baskets' refresh as a service (<see cref="Id"/>, spec BK-01): every day at 07:30 IST it carries out <see cref="RunBasketFetchCommand"/> for the enabled index lists (one conditional GET per file). It starts <b>paused</b>; while
/// <c>Baskets:System:Fetch:Enabled</c> is off a run makes no request and ends as skipped (disabled). Three retries (four tries); it cannot place an order.
/// </summary>
public sealed class SystemBasketsService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "system-baskets";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "System baskets",
        "Every morning at 07:30 reads the official NSE index lists (one conditional request per enabled file) and keeps the index baskets' membership with dates. Fails closed: a file that looks wrong is rejected and the last good list stays. Off by default; it never places an order.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(7, 30)), 3, 10, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { RecoveryClass = RecoveryClass.LatestOnly, CatchUpReach = CatchUpReach.Latest });

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
            return result.Error.Code == BasketErrors.FetchDisabledCode ? ServiceOutcome.Skipped("disabled") : ServiceOutcome.Failed(result.Error.Message);
        }

        return result.Value.Outcomes.Any(o => o.Outcome == FetchOutcomes.Failed) ? ServiceOutcome.Failed(result.Value.Summary) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
