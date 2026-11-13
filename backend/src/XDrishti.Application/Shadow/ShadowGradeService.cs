using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The shadow grader as a service (<see cref="Id"/>, spec CF-08 part B, ADR 0212): every trading day at 17:30, after the end-of-day data run has stored the bars of the session (like the paper capture, which runs at 17:00), it carries out <see cref="GradeShadowCommand"/>:
/// every shadow candidate that counts for edge and has no grade is replayed on the stored official bars with the grading rules and gets one insert-only grade. It starts <b>enabled</b> (ADR 0282), and it grades nothing while the setting
/// <c>paper.shadow.enabled</c> (default ON from the seed row) is OFF. It places no order, calls no broker and moves no ticket.
/// </summary>
public sealed class ShadowGradeService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "shadow-grade";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Shadow grading",
        "At 17:30, after the end-of-day data run, replays every candidate of the shadow record that counts for edge and is not graded yet, on the stored official 1-minute bars with the grading rules (the same replay as grading and the paper capture), and stores one grade each: R, entry, exit and reason. A candidate the replay cannot judge yet is tried again on the next run. It grades only when the setting paper.shadow.enabled is ON (it is ON by default, ADR 0282). The record is simulated: no order was placed and none exists.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(17, 30)), 0, 15, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.Convergent,
            MustFollow = [EodService.Id],
            StartNotBefore = new TimeOnly(16, 30),
            StartAdvice = "It must start after the end-of-day data run: the bars of the session must be stored, or the candidates stay pending until the next run.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();

        // the replay must not run on a hole the end-of-day proof found (a grade over a missing minute is never graded again): the run is skipped, so a recovery pass waits and tries again; the owner's own run is not held back
        if (context.Trigger != ServiceRunTrigger.Manual && await scope.ServiceProvider.GetRequiredService<IRecoveryDataGuard>().WhyUnprovenAsync(null, cancellationToken) is { } why)
        {
            return ServiceOutcome.Skipped(why);
        }

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<GradeShadowCommand, ShadowGradeOutcome>>();
        var result = await handler.HandleAsync(new GradeShadowCommand(), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        return result.Value.Failed > 0 ? ServiceOutcome.Failed(result.Value.Summary) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
