using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Learning.CellsRefresh;

/// <summary>
/// The scheduled cell-statistics refresh (<see cref="Id"/>, spec LC-01, ADR 0277): after the end-of-day run it carries out <see cref="RefreshCellStatsCommand"/>. It starts <b>paused</b>; resuming it is an owner act with a fresh password, because it writes a decision input with no human in
/// between. Latest only (ADR 0240): one run writes one snapshot as of the latest closed session. It is not in the nightly's <c>MustFollow</c>: a paused refresh must not block the nightly's recovery; the gate is the check. It fails closed: on any failure it stores no snapshot and re-dates nothing, so a failure never becomes a stale pass. It places no order.
/// </summary>
public sealed class CellsRefreshService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = CellsRefreshRules.ServiceId;

    /// <summary>Recorded as the author of a scheduled refresh.</summary>
    public const string Requester = "system:cells-refresh";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Cell statistics refresh",
        "After the end-of-day run, recomputes the cell statistics as of the last closed session from the sources of the newest snapshot, so the nightly plan's selection-inputs gate sees fresh figures. Sources whose strategy version is pre-registered are simulated again over a window that ends at the hold-out cut (less the embargo); the rest are kept. Fails closed and stores nothing on any failure. Before a hold-out block is released no new trades can enter, so each run only re-dates the same trades. Resuming it needs your password. It never places an order.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Daily],
        new ServiceSettings(false, Schedule.AfterService(EodService.Id), 0, 120, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.LatestOnly,
            MustFollow = [EodService.Id],
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
            ResumeIsOwnerAct = true,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RefreshCellStatsCommand, RefreshCellStatsOutcome>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new RefreshCellStatsCommand(manual ? context.RequestedBy : Requester), cancellationToken);
        return result.IsFailure ? ServiceOutcome.Failed(Clip(result.Error.Code + ": " + result.Error.Message)) : ServiceOutcome.Succeeded(Clip(result.Value.Message));
    }

    private static string Clip(string text) => text.Length <= ServiceRun.MaxMessageLength ? text : text[..(ServiceRun.MaxMessageLength - 1)] + "~";
}
