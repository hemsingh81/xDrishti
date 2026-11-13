using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Paper;

/// <summary>
/// The paper capture as a service (<see cref="Id"/>, spec P10-01, ADR 0116): every trading day at 17:00, after the end-of-day data run and before the nightly plan, it carries out <see cref="CapturePaperFillsCommand"/>: every armed ticket of a closed
/// session becomes a timed, priced paper trade per account, replayed on the stored official bars. It starts <b>enabled</b> (ADR 0282); switching it on is the owner's act. It places no order, calls no broker and moves no ticket.
/// </summary>
public sealed class PaperCaptureService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "paper-capture";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Paper capture",
        "At 17:00, after the end-of-day data run, replays every ticket you approved and that was armed, on the stored official 1-minute bars with the grading rules, and records one paper trade per account at your size: entry, exit, rupees and R. The record is simulated: no order was placed and none exists. A failed run is shown here with its reason.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(17, 0)), 0, 15, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.TimeBound,
            MustFollow = [EodService.Id],
            StartNotBefore = new TimeOnly(16, 30),
            StartAdvice = "It must start after the end-of-day data run (the bars of the session must be stored) and before the nightly plan (18:30).",
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

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CapturePaperFillsCommand, PaperCaptureOutcome>>();
        var result = await handler.HandleAsync(new CapturePaperFillsCommand(null, context.Trigger == ServiceRunTrigger.Manual), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        return result.Value.FailedSessions > 0 ? ServiceOutcome.Failed(result.Value.Summary) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
