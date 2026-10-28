using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The evening settle of the live sessions as a service (<see cref="Id"/>, spec P11-01, ADR 0149): after the history read, it stores the charges of the day's delivery fills, matches the fills to the approved tickets and writes the one outcome of each ticket,
/// final, then (P11-02) measures each entry against its reference and classifies each entry order for the capacity score. Sessions whose plan grade is not stored yet are tried again by the next run (the last seven days are looked at). It starts <b>paused</b> and reads Dhan's history only (GET); it places no order.
/// </summary>
public sealed class LiveSettleService(IServiceScopeFactory scopes, ILiveCaptureSettings settings) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "live-settle";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Live settle",
        "After the trades and ledger are read from Dhan, matches what you did to the tickets you approved and gives each one a single, final outcome: filled, partial, rejected, missed (and why) or not triggered. Reads only; it places nothing. It starts paused.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Daily],
        new ServiceSettings(false, Schedule.AfterService(PortfolioBrokerHistoryService.Id), 0, 15, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            MustFollow = [PortfolioBrokerHistoryService.Id],
            StartAdvice = "It follows the evening read of Dhan's trades and ledger; the plan grade of a session is stored by the grading run, and a ticket whose grade is not there yet is settled by a later run.",
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    /// <summary><c>Live:Capture:Enabled</c> (default off) seeds this service too.</summary>
    public ServiceSettings? FirstStartSettings() => Definition.Defaults with { Enabled = settings.Enabled };

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SettleLiveSessionCommand, LiveSettleOutcome>>();
        var result = await handler.HandleAsync(new SettleLiveSessionCommand(null, null), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        // The step after the settle (spec P11-02): measure what the owner did against the plan. It reads stored data and writes insert-only rows; a failure of it is reported but never undoes the outcomes.
        var measured = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeLiveExecutionsCommand, LiveExecutionsOutcome>>().HandleAsync(new ComputeLiveExecutionsCommand(null, null), cancellationToken);
        // The ramp after the measure (spec P11-04): holds, step-downs, halts and, only with live.auto_ramp_up ON, the automatic step-up. Rows by rule:ramp; a failure of it is reported but never undoes the outcomes.
        var evaluated = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateRampCommand, RampEvaluationOutcome>>().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: false), cancellationToken);
        var ramp = evaluated.IsSuccess ? "; " + evaluated.Value.Summary : "; ramp evaluation failed";
        var summary = (measured.IsSuccess ? result.Value.Summary + "; " + measured.Value.Summary : result.Value.Summary + "; execution step failed") + ramp;
        var stepFailed = measured.IsFailure || measured.Value.Failed > 0 || evaluated.IsFailure || evaluated.Value.Failed > 0;
        return result.Value.Failed > 0 || stepFailed ? ServiceOutcome.Failed(summary) : result.Value.Sessions == 0 && !(measured.IsSuccess && measured.Value.Sessions > 0) ? ServiceOutcome.Skipped("No live session to settle") : ServiceOutcome.Succeeded(summary);
    }
}
