using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Planning;

/// <summary>
/// The nightly plan as a service (<see cref="Id"/>, spec P7-08, ADR 0086, ADR 0018): one run carries out <see cref="RunNightlyPlanCommand"/> for the last closed session. <b>When</b> it runs is the scheduler's: every trading day at 18:30,
/// never before the session has ended, after the end-of-day data run and the portfolio sync (the gates check that they succeeded). It starts <b>paused</b>: until the real builder (P7-04/P7-09) exists it can only publish an empty plan marked skeleton,
/// so the owner switches it on. A gate that does not pass ends the run as <i>failed</i> (visible on the Services screen and the bell) with the gate and reason; nothing is published. A session with a plan already is a success that does nothing.
/// </summary>
public sealed class NightlyService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "nightly";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Nightly plan",
        "Checks that the day's data is complete and the inputs are there (fail closed: any gate that does not pass publishes nothing), then publishes the proposed plan for the next session. Today it publishes an empty skeleton plan; it never places an order and never approves a ticket.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(18, 30)), 0, 30, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            MustFollow = [EodService.Id, PortfolioBrokerHistoryService.Id],
            StartNotBefore = new TimeOnly(16, 30),
            StartAdvice = "The end-of-day data run (15:50) and the portfolio sync (16:30) must have finished; the gates fail the plan closed if they have not.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunNightlyPlanCommand, NightlyRunView>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new RunNightlyPlanCommand(null, false, manual, manual ? context.RequestedBy : "system:nightly"), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        var view = result.Value;
        return view.Outcome == NightlyOutcomes.Published
            ? ServiceOutcome.Succeeded(view.Repeated ? "Already published: " + view.Reason : view.Reason)
            : ServiceOutcome.Failed($"Fail closed at {view.FailedGate}: {view.Reason}");
    }
}
