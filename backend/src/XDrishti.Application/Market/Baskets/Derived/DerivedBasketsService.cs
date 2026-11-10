using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// The derived baskets' refresh as a service (<see cref="Id"/>, spec BK-03): after the end-of-day run (<c>eod</c>) it carries out <see cref="RefreshDerivedBasketsCommand"/> from our own bars, one version per enabled basket that is due. It starts <b>paused</b>; while
/// <c>Baskets:System:Derived:Enabled</c> is off a run reads and writes nothing and ends as skipped (disabled). A missed run is not replayed (latest only): the next one forms the then-latest version. It cannot place an order.
/// </summary>
public sealed class DerivedBasketsService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "derived-baskets";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Derived baskets",
        "After the end-of-day run, forms the behaviour baskets (most liquid, most volatile, momentum and the like) from our own daily bars: one dated version per basket, effective from the next session, with its scores and named exclusions. No network. Off by default; it never places an order.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Daily],
        new ServiceSettings(false, Schedule.AfterService(EodService.Id), 3, 20, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { RecoveryClass = RecoveryClass.LatestOnly, MustFollow = [EodService.Id], CatchUpReach = CatchUpReach.Latest });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RefreshDerivedBasketsCommand, DerivedRun>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new RefreshDerivedBasketsCommand(manual ? context.RequestedBy : "system:derived-baskets", manual), cancellationToken);
        if (result.IsFailure)
        {
            // A held lock (a long backfill) fails the run, so the service's retries (3) form the day's version once it is free, instead of losing the day.
            return result.Error.Code == BasketErrors.DerivedDisabledCode ? ServiceOutcome.Skipped("disabled")
                : result.Error.Code == BasketErrors.DerivedRunningCode && manual ? ServiceOutcome.Skipped("already running") : ServiceOutcome.Failed(result.Error.Message);
        }

        return ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
