using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Planning;

/// <summary>
/// The review cutoff as a service (<see cref="Id"/>, spec P7-10, ADR 0095, ADR 0018): every minute from 09:03 to 09:10 on trading days it carries out <see cref="RunReviewCycleCommand"/> for today's plan. Before the plan's cutoff (09:05) it does
/// nothing, except that in the last two minutes it runs the optional automatic approval, and only when the owner has switched that setting ON (default OFF: manual approval). At and after the cutoff it lapses every ticket nobody decided on.
/// It starts <b>paused</b>; the owner switches it on. It approves nothing by itself and cannot place an order.
/// </summary>
public sealed class ReviewCutoffService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "review-cutoff";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Review cutoff",
        "Closes the morning review of the day's plan: at the plan's review cutoff (09:05) every ticket nobody decided on lapses. In the last two minutes before it, and only if you have switched automatic approval ON (it is OFF by default), the approval rule may approve tickets whose 14 checks all passed. It never places an order.",
        "xd-api",
        [ScheduleType.Interval, ScheduleType.Window, ScheduleType.Daily],
        new ServiceSettings(false, Schedule.Interval(1, new TimeOnly(9, 3), new TimeOnly(9, 10)), 0, 2, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            StartNotAfter = new TimeOnly(9, 5),
            StartAdvice = "It must run through the cutoff time (09:05) of the plan: unreviewed tickets lapse only when it runs at or after it, and the pre-open check follows at 09:10.",
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunReviewCycleCommand, ReviewCycleOutcome>>();
        var result = await handler.HandleAsync(new RunReviewCycleCommand(), cancellationToken);
        return result.IsFailure ? ServiceOutcome.Failed(result.Error.Message) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
