using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Learning;

/// <summary>
/// The weekly service (<see cref="Id"/>, spec P7-15, ADR 0094, ADR 0018): one run builds the learning digest of the current ISO week from stored records and stores it (once per week). It starts <b>paused</b>, Saturday 10:00.
/// It runs no P6 verb that writes: it does not compute cell statistics, replay drift, open lessons, propose or judge hypotheses, move a cell, designate a model or approve anything. Every item in the digest is a proposal for the owner (Q6, Q16).
/// </summary>
public sealed class WeeklyService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "weekly";

    /// <summary>Recorded as the builder of a scheduled digest.</summary>
    public const string Requester = "system:weekly";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Weekly learning digest",
        "Builds the week's learning digest from stored records (cells, drift and loss-tag proposals, lessons, hypotheses, plan outcomes, model status, honest limits) and stores it once per ISO week. It only lists proposals: it applies nothing, approves nothing and places no order.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(false, Schedule.Weekly(DayOfWeek.Saturday, new TimeOnly(10, 0)), 0, 30, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            CatchUpReach = CatchUpReach.SameDay, // a weekly job missed on Saturday is caught up within 24 hours (Sunday); a later start would store a digest of a past week
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildDigestCommand, DigestView>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new BuildDigestCommand(null, true, manual ? context.RequestedBy : Requester), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        var view = result.Value;
        return ServiceOutcome.Succeeded(view.Repeated ? $"Digest {view.IsoWeek} was stored already; nothing written." : $"Digest {view.IsoWeek} stored (id {view.Id}); nothing applied.");
    }
}
