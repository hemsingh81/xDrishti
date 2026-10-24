using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Planning;

/// <summary>
/// The pre-open check as a service (<see cref="Id"/>, spec P7-11, ADR 0096, ADR 0018): every trading day at 09:10, after the review cutoff (09:05) and before the open (09:15), it carries out <see cref="RunPreOpenCheckCommand"/>: each approved
/// ticket of today's plan is armed (watched with alerts, in the live feed's set) or invalidated (a gap beyond the limit, or a missing input). It starts <b>paused</b>; the owner switches it on. It does not catch up (a late start would be
/// after the open, and the handler refuses after 09:15 anyway). It approves nothing, promotes nothing from the reserve and cannot place an order.
/// </summary>
public sealed class PreOpenService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "preopen";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Pre-open check",
        "At 09:10, after the review cutoff and before the open, checks every ticket you approved against the pre-open price (the gap to the previous close against half the average true range; a missing price or range counts as a fail) and arms the ones that pass: they are watched in the live feed with alerts. No order is placed and none exists; a stop is only an alert. Nothing is approved or taken from the reserve.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(9, 10)), 0, 4, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            MustFollow = [ReviewCutoffService.Id],
            StartNotBefore = new TimeOnly(9, 5),
            StartNotAfter = new TimeOnly(9, 14),
            StartAdvice = "It must start after the review cutoff (09:05) and before the open (09:15); after 09:15 it arms nothing.",
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunPreOpenCheckCommand, PreOpenOutcome>>();
        var result = await handler.HandleAsync(new RunPreOpenCheckCommand(), cancellationToken);
        return result.IsFailure ? ServiceOutcome.Failed(result.Error.Message) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
