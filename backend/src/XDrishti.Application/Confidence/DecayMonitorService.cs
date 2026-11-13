using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The decay monitor as a service (<see cref="Id"/>, spec CF-12 part 12b, ADR 0219): every trading day at 18:00, after the shadow grading of 17:30, it carries out <see cref="EvaluateDecayCommand"/>: each strategy with shadow edge candidates in the current paper epoch is
/// evaluated once for the day, a firing kill rule writes a proposal (and a bell notice is derived from it), and only when the setting <c>confidence.kill.auto</c> is ON (ON by default through the system default row of ADR 0282; the owner can switch it off with a fresh password) are the proposals that are not proposal-only applied, as the actor <c>auto</c>.
/// It starts <b>enabled</b> (ADR 0282). A failed strategy records nothing for the day and the run reports the failure. It re-enables nothing, calls no broker and places no order.
/// </summary>
public sealed class DecayMonitorService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "decay-monitor";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Decay monitor",
        "At 18:00, after the shadow grading, evaluates each strategy's forward edge series once a day against the kill rules (cost stress, drawdown, decay twice in a row, and the CF-09 failure boundary as a proposal-only input) and writes a proposal and a bell notice for each rule that fires. It pauses cells only while confidence.kill.auto is ON (ON by default since ADR 0282, through a system default row; the owner can switch it off), and then only for the rules that are not proposal-only. Re-enabling a paused cell is always the owner's act. The series is simulated: no order was placed and none exists.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(18, 0)), 0, 15, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.TimeBound,
            MustFollow = [ShadowGradeService.Id],
            StartNotBefore = new TimeOnly(17, 30),
            StartAdvice = "It must start after the shadow grading: the day's grades must be stored, or the series stops before the ungraded session.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateDecayCommand, DecayRunOutcome>>();
        var result = await handler.HandleAsync(new EvaluateDecayCommand(), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        return result.Value.Failed > 0 ? ServiceOutcome.Failed(result.Value.Summary) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
