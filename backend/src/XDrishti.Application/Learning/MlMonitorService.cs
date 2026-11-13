using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Learning;

/// <summary>
/// The ML monitor as a service (<see cref="Id"/>, spec CF-10 part 10d-1, ADR 0220): every trading day at 18:15, after the decay monitor of 18:00, it carries out <see cref="EvaluateMlModelsCommand"/>: each champion is evaluated once for the day against the rollback triggers (the P6-09
/// calibration detector, and the rolling band detector on the cells it scores), a firing trigger writes a proposal (and a bell notice is derived from it), and only when the owner's stored setting <c>ml.auto_rollback</c> (default OFF) is ON are the open proposals applied, as the actor
/// <c>auto</c>; when <c>ml.auto_promote</c> (default OFF) is ON a challenger that meets the promotion rule and the predicate without the switch is promoted as <c>auto</c>. It starts <b>enabled</b> (ADR 0282). A failed model records nothing for the day and the run reports the failure.
/// It re-promotes nothing, sets no switch, calls no broker and places no order.
/// </summary>
public sealed class MlMonitorService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "ml-monitor";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "ML monitor",
        "At 18:15, after the decay monitor, evaluates each champion model once a day against the rollback triggers (the calibration detector, and the rolling band on the cells it scores) and writes a rollback proposal and a bell notice for each that fires. It demotes the champion only if the owner's setting ml.auto_rollback is ON (it is OFF by default); re-promotion is always the owner's act. When ml.auto_promote is ON (OFF by default) it promotes a challenger only if the promotion rule and the influence predicate (without the owner's switch) hold; no source of that evidence exists yet, so it promotes nothing. A model is advisory only, and no order exists.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(18, 15)), 0, 15, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.TimeBound,
            MustFollow = [DecayMonitorService.Id],
            StartNotBefore = new TimeOnly(18, 0),
            StartAdvice = "It must start after the decay monitor, so that the day's evaluations and the bell are in order.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateMlModelsCommand, MlRunOutcome>>();
        var result = await handler.HandleAsync(new EvaluateMlModelsCommand(), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        return result.Value.Failed > 0 ? ServiceOutcome.Failed(result.Value.Summary) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
