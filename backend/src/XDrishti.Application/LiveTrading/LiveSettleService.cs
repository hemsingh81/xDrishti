using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The evening settle of the live sessions as a service (<see cref="Id"/>, spec P11-01, ADR 0149): after the history read, it stores the charges of the day's delivery fills, matches the fills to the approved tickets and writes the one outcome of each ticket,
/// final. Sessions whose plan grade is not stored yet are tried again by the next run (the last seven days are looked at). It starts <b>paused</b> and reads Dhan's history only (GET); it places no order.
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

        return result.Value.Failed > 0 ? ServiceOutcome.Failed(result.Value.Summary) : result.Value.Sessions == 0 ? ServiceOutcome.Skipped("No live session to settle") : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
