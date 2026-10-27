using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>
/// The weekly review service (<see cref="Id"/>, spec P9-06, ADR 0140): after the weekly digest it stores the review text of the current week, built from the digest and the report insights. It starts <b>paused</b>; the model is used only under the same two switches as the briefing.
/// The digest stays the source and is never changed; the text ends with the pinned line that nothing was applied.
/// </summary>
public sealed class WeeklyReviewService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "weekly-review";

    /// <summary>Recorded as the builder of a scheduled review.</summary>
    public const string Requester = "system:weekly-review";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Weekly review text",
        "After the weekly digest, stores a short review text of the week built from the digest and the report insights. Text only: every item is a proposal and nothing is applied.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Weekly],
        new ServiceSettings(false, Schedule.AfterService(WeeklyService.Id), 0, 240, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            MustFollow = [WeeklyService.Id],
            StartAdvice = "It reads the digest the weekly service stored; run it after the digest.",
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildBriefingCommand, BriefingView>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new BuildBriefingCommand(BriefingFacts.Weekly, null, false, false, manual ? context.RequestedBy : Requester), cancellationToken);
        return BriefingOutcomes.Of(result);
    }
}
