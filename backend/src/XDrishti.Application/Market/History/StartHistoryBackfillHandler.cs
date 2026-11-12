using System.Globalization;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Market.Bars;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.History;

/// <summary>Starts the whole-set backfill as the service <c>history-backfill</c> (CF-04e decision D4). The password was checked before this runs; the actor is the session owner.</summary>
public sealed record StartHistoryBackfillCommand(string Actor) : ICommand;

/// <summary>The answer: <see cref="Started"/> false when nothing is pending (no run); else the service run that carries the work.</summary>
public sealed record HistoryBackfillStarted(bool Started, Guid? RunId, int ChunksPending, long EstimatedRows, long EstimatedRequests, double EstimatedMinutes, string Text);

/// <summary>
/// Order after the password: run active, run window, the dry run (calendar, disk; zero requests), the Dhan availability, then the service starts through the manual "Run now" path. Every refusal after the password is audited as <c>refused=&lt;code&gt;</c>.
/// </summary>
internal sealed class StartHistoryBackfillHandler(
    HistoryActGuard guard,
    HistoryProbeGate gate,
    ICommandHandler<RunBackfillCommand, BackfillSummary> dryRun,
    ICommandHandler<RunServiceNowCommand, ServiceRunView> runNow,
    IIntradayBarSource source,
    IDataAccountStatus account) : ICommandHandler<StartHistoryBackfillCommand, HistoryBackfillStarted>
{
    public async ValueTask<Result<HistoryBackfillStarted>> HandleAsync(StartHistoryBackfillCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!HistoryActErrors.IsOwner(command.Actor))
        {
            return Result.Failure<HistoryBackfillStarted>(HistoryActErrors.OwnerOnly());
        }

        if (!gate.TryEnter())
        {
            return await RefusedAsync(HistoryActErrors.RunActive(), command.Actor); // a probe (or another start) is in the request now
        }

        try
        {
            return await StartAsync(command, cancellationToken);
        }
        finally
        {
            gate.Leave();
        }
    }

    private async ValueTask<Result<HistoryBackfillStarted>> StartAsync(StartHistoryBackfillCommand command, CancellationToken cancellationToken)
    {
        if (await guard.CheckAsync(cancellationToken) is { } refusal)
        {
            return await RefusedAsync(refusal, command.Actor);
        }

        var planned = await dryRun.HandleAsync(new RunBackfillCommand(DryRun: true), cancellationToken);
        if (planned.IsFailure)
        {
            return await RefusedAsync(planned.Error, command.Actor);
        }

        var plan = planned.Value;
        if (plan.ChunksPending == 0)
        {
            await guard.AuditAsync(AccountEventType.HistoryBackfillStarted, "pending=0 estimated_requests=0", command.Actor);
            return Result.Success(new HistoryBackfillStarted(false, null, 0, 0, 0, 0, plan.Text));
        }

        var availability = await source.CheckAsync(cancellationToken);
        if (!availability.Available)
        {
            return await RefusedAsync(Error.Conflict(DataErrors.DhanNotAvailableCode, availability.Reason ?? "The Dhan Data API fetch is not available."), command.Actor);
        }

        var state = await account.GetAsync(cancellationToken);
        if (!state.CanFetch)
        {
            return await RefusedAsync(Error.Conflict(DataErrors.DhanNotConnectedCode, state.Reason ?? "There is no connected primary Data account."), command.Actor);
        }

        var started = await runNow.HandleAsync(new RunServiceNowCommand(HistoryBackfillService.Id, command.Actor, ByOwnerAct: true), cancellationToken);
        if (started.IsFailure)
        {
            return await RefusedAsync(started.Error.Code == ServiceErrors.AlreadyRunningCode ? HistoryActErrors.RunActive() : started.Error, command.Actor);
        }

        await guard.AuditAsync(AccountEventType.HistoryBackfillStarted, string.Create(CultureInfo.InvariantCulture, $"pending={plan.ChunksPending} estimated_requests={plan.EstimatedRequests} run_id={started.Value.Id:D}"), command.Actor);
        return Result.Success(new HistoryBackfillStarted(true, started.Value.Id, plan.ChunksPending, plan.EstimatedRows, plan.EstimatedRequests, plan.EstimatedMinutes, plan.Text));
    }

    private async ValueTask<Result<HistoryBackfillStarted>> RefusedAsync(Error error, string actor)
    {
        await guard.AuditAsync(AccountEventType.HistoryBackfillStarted, "refused=" + error.Code, actor);
        return Result.Failure<HistoryBackfillStarted>(error);
    }
}
