using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Planning;

/// <summary>
/// The suggestions service (<see cref="Id"/>, spec P7-09, ADR 0092, ADR 0018): each time the nightly plan finishes successfully it produces the owner's suggestions for the newest plan by deterministic rules (no model) and appends the new ones to the
/// inbox. It starts <b>paused</b>; the owner switches it on. It only reads plans and writes suggestions: it approves nothing and cannot place an order.
/// </summary>
public sealed class SuggestionsService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "suggestions";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Suggestions",
        "After the nightly plan, writes the owner's suggestions for it (a gate with no data, no primary ticket, tickets held for an unavailable input, stale statistics) by fixed rules. Advice only: it changes no plan and approves nothing.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Daily, ScheduleType.Weekly],
        new ServiceSettings(false, Schedule.AfterService(NightlyService.Id), 0, 5, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            MustFollow = [NightlyService.Id],
            StartAdvice = "It reads the plan the nightly run stored; run it after the nightly plan.",
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<GenerateSuggestionsCommand, SuggestionsOutcome>>();
        var result = await handler.HandleAsync(new GenerateSuggestionsCommand(null), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        var outcome = result.Value;
        return outcome.PlanId is null
            ? ServiceOutcome.Succeeded("No plan is stored yet; nothing to suggest.")
            : ServiceOutcome.Succeeded($"Plan {outcome.ForDate:yyyy-MM-dd}: {outcome.Produced} suggestion(s), {outcome.Stored} new.");
    }
}
