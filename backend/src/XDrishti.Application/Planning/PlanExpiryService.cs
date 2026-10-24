using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Planning;

/// <summary>
/// The end of the session for the day's tickets as a service (<see cref="Id"/>, spec P7-11, ADR 0096): every trading day at 15:40, after the close, it carries out <see cref="ExpireTicketsCommand"/>: armed tickets (and approved ones that were never
/// armed) expire and leave the live feed's set. It starts <b>paused</b>. Missed days are closed at the next run. It approves nothing and cannot place an order.
/// </summary>
public sealed class PlanExpiryService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "plan-expiry";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Plan expiry",
        "At 15:40, after the close, ends the day for the plan's tickets: armed tickets expire and their instruments leave the live feed. Also closes any earlier plan still open. It never places an order. Price alerts on trigger and stop are not built yet; only the live price is shown.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(15, 40)), 0, 4, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            StartNotBefore = new TimeOnly(15, 30),
            StartAdvice = "It must start after the close (15:30): before it, armed tickets would leave the feed during the session.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpireTicketsCommand, ExpiryOutcome>>();
        var result = await handler.HandleAsync(new ExpireTicketsCommand(), cancellationToken);
        return result.IsFailure ? ServiceOutcome.Failed(result.Error.Message) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
