using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Paper;

/// <summary>
/// The weekly paper review as a service (<see cref="Id"/>, spec P10-03, ADR 0121, ADR 0018): Saturday 10:30 India time, after the weekly digest, it builds the review of the current week and stores it once per week and epoch. It starts <b>paused</b>; switching it
/// on is the owner's act. It reads stored records and writes only that text: no setting, ticket, approval or order is touched.
/// </summary>
public sealed class PaperReviewService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "paper-review";

    /// <summary>Recorded as the builder of a scheduled review.</summary>
    public const string Requester = "system:paper-review";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Weekly paper review",
        "Saturday 10:30, after the weekly digest, builds the week's paper review (where the epoch stands, what happened, what you skipped, added or resized, execution gap, configuration changes) from stored records and stores it once per ISO week and epoch. It applies nothing, approves nothing and places no order.",
        "xd-api",
        [ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(false, Schedule.Weekly(DayOfWeek.Saturday, new TimeOnly(10, 30)), 0, 30, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            MustFollow = [WeeklyService.Id],
            CatchUpReach = CatchUpReach.SameDay, // missed on Saturday: caught up within 24 hours; a later start would store a review of a past week
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildPaperReviewCommand, PaperReviewView>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new BuildPaperReviewCommand(null, null, true, manual ? context.RequestedBy : Requester), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        var view = result.Value;
        return ServiceOutcome.Succeeded(view.Repeated ? $"Paper review {view.Week} was stored already; nothing written." : $"Paper review {view.Week} stored (id {view.Id}); nothing applied.");
    }
}
