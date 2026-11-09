using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The daily readiness snapshots as a service (<see cref="Id"/>, spec CF-11 D7, ADR 0221): every trading day at 18:30, after the decay monitor of 18:00, it carries out <see cref="SnapshotScorecardsCommand"/>: one insert-only snapshot per Lab strategy and one for the system, with the
/// settings hash, once a day. It starts <b>paused</b>. A strategy that fails records nothing for the day and the run reports the failure. It reads and stores a derived score: it moves no cell, raises no size, calls no broker and places no order.
/// </summary>
public sealed class ReadinessSnapshotService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "readiness-snapshot";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Readiness snapshots",
        "At 18:30, after the decay monitor, stores one snapshot of the readiness score of each strategy and one of the system, with the hash of the settings used, once a day, so that a trend can be drawn and a gate decision can cite what was true. It reads stored results and writes only its own snapshot rows: it moves no cell, raises no size and places no order. A strategy whose scorecard cannot be built is counted and repeated the next day.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(18, 30)), 0, 15, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.LatestOnly,
            MustFollow = [DecayMonitorService.Id],
            StartNotBefore = new TimeOnly(18, 0),
            StartAdvice = "It must start after the decay monitor, so that the day's kill-rule state is stored before the score is read.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SnapshotScorecardsCommand, SnapshotRunOutcome>>();
        var result = await handler.HandleAsync(new SnapshotScorecardsCommand(), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        return result.Value.Failed > 0 ? ServiceOutcome.Failed(result.Value.Summary) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
