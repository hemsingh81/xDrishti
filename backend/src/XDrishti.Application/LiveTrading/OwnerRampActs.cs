using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The owner's two other ramp acts (spec P11-04, ADR 0151): clearing a hold and recording the exit confirmation. Both ask the live-writes gate again, need an owner actor and a reason, append one insert-only event and one audit event with the numbers (never the
/// reason text). The endpoint is responsible for the session and the fresh password. A hold is cleared only when no breach condition still holds on the evidence read now; the exit confirmation is recorded only when the P11-06 report is MET right now (the
/// owner reads it, the system does not decide; the report is not built yet, so it is refused). Nothing here calls a broker or places an order.
/// </summary>
internal sealed class OwnerRampActs(
    IAccountStore accounts,
    IRampStore ramp,
    GetRampHandler view,
    RampAssessor assessor,
    ILiveExitReport exitReport,
    ILiveWritesGate gate,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<OwnerRampActs> logger) : ICommandHandler<ClearHoldCommand, RampView>, ICommandHandler<ConfirmExitCommand, RampView>
{
    public async ValueTask<Result<RampView>> HandleAsync(ClearHoldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await RunAsync(command.AccountId, command.Actor, command.Reason, async (account, current) =>
        {
            var assessment = current is null ? null : await assessor.AssessAsync(account, cancellationToken);
            return RampEvent.OwnerClearHold(account.Id, current, command.Actor, command.Reason, assessment?.HoldConditions ?? []);
        }, cancellationToken);
    }

    public async ValueTask<Result<RampView>> HandleAsync(ConfirmExitCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await RunAsync(command.AccountId, command.Actor, command.Reason, async (account, current) =>
        {
            var check = await exitReport.CheckAsync(cancellationToken);
            return check.Met
                ? RampEvent.OwnerExitConfirmed(account.Id, current, command.Actor, command.Reason)
                : Result.Failure<RampEvent>(Error.Conflict(RampErrors.ExitNotCheckedCode, "The small-live exit report is not met (or cannot be read): " + check.Reason));
        }, cancellationToken);
    }

    private async ValueTask<Result<RampView>> RunAsync(Guid accountId, string actor, string? reason, Func<BrokerAccount, RampState?, ValueTask<Result<RampEvent>>> make, CancellationToken cancellationToken)
    {
        if (!gate.Enabled)
        {
            return Refuse(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."));
        }

        var account = await accounts.FindReadOnlyAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Refuse(Error.NotFound(LiveErrors.AccountNotFound, "The account does not exist."));
        }

        var made = await make(account, await ramp.CurrentAsync(accountId, cancellationToken));
        if (made.IsFailure)
        {
            return Refuse(made.Error);
        }

        var stored = await ramp.AppendAsync(made.Value, cancellationToken);
        if (stored is null)
        {
            return Refuse(Error.Conflict(LiveErrors.Stale, "The ramp changed meanwhile; read it again."));
        }

        RampLog.StepSet(logger, stored.Id, stored.FromStep, stored.ToStep);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.RampStepSet, accountId, string.Create(CultureInfo.InvariantCulture, $"event={stored.Id} kind={stored.Kind} step={stored.ToStep}"), actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            RampLog.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(await view.ViewAsync(account, cancellationToken));
    }

    private Result<RampView> Refuse(Error error)
    {
        RampLog.Refused(logger, error.Code);
        return Result.Failure<RampView>(error);
    }
}
