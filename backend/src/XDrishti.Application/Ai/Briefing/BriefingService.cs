using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Planning;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>
/// The daily briefing service (<see cref="Id"/>, spec P9-06, ADR 0140, ADR 0018): after the suggestions it stores the briefing text of the newest plan. It starts <b>paused</b> and, with <c>Assistant:Briefing:UseModel</c> false (the default), stores the fixed-format text only.
/// It is a service of its own: the nightly plan and the suggestions never wait for it, and its failure is a failed service run only. It writes one insert-only text; it places no order and approves nothing.
/// </summary>
public sealed class BriefingService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "briefing";

    /// <summary>Recorded as the builder of a scheduled briefing.</summary>
    public const string Requester = "system:briefing";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Daily briefing",
        "After the suggestions, stores the day's briefing text built from the stored plan. A local model may phrase it when both the assistant and the briefing switch are on; a checker rejects any figure not in the stored facts and the fixed-format text is stored instead. Text only: it changes no plan and places no order.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Daily],
        new ServiceSettings(false, Schedule.AfterService(SuggestionsService.Id), 0, 240, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            MustFollow = [SuggestionsService.Id],
            StartAdvice = "It reads the plan and the suggestions of the nightly run; run it after them. With the briefing switch off it stores the fixed-format text.",
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildBriefingCommand, BriefingView>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new BuildBriefingCommand(BriefingFacts.Daily, null, false, false, manual ? context.RequestedBy : Requester), cancellationToken);
        return BriefingOutcomes.Of(result);
    }
}
