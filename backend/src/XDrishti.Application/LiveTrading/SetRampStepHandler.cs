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
/// The owner's act of setting the ramp step of an account (spec P11-04, ADR 0148). Asks the live-writes gate again, applies <see cref="RampEvent.OwnerSetStep"/> (owner actor, a reason, S1 first, no skip upwards, no raise before the gates exist) and appends one row; then one account
/// event <c>RampStepSet</c> with the numbers - never the reason text. The endpoint is responsible for the session and the fresh password. Nothing here calls a broker or places an order.
/// </summary>
internal sealed class SetRampStepHandler(
    IAccountStore accounts,
    IRampStore ramp,
    GetRampHandler view,
    ILiveWritesGate gate,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<SetRampStepHandler> logger) : ICommandHandler<SetRampStepCommand, RampView>
{
    public async ValueTask<Result<RampView>> HandleAsync(SetRampStepCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!gate.Enabled)
        {
            return Refuse(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."));
        }

        var account = await accounts.FindReadOnlyAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Refuse(Error.NotFound(LiveErrors.AccountNotFound, "The account does not exist."));
        }

        if (!account.Trading.Enabled)
        {
            return Refuse(RampErrors.Invalid("accountId", "The account's Trading role is off: a ramp step belongs to an account that trades."));
        }

        var current = await ramp.CurrentAsync(command.AccountId, cancellationToken);
        var made = RampEvent.OwnerSetStep(command.AccountId, current, command.ToStep, command.Actor, command.Reason);
        if (made.IsFailure)
        {
            return Refuse(made.Error);
        }

        var stored = await ramp.AppendAsync(made.Value, cancellationToken);
        if (stored is null)
        {
            return Refuse(Error.Conflict(LiveErrors.Stale, "The step changed meanwhile; read it again."));
        }

        RampLog.StepSet(logger, stored.Id, stored.FromStep, stored.ToStep);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.RampStepSet, command.AccountId, string.Create(CultureInfo.InvariantCulture, $"event={stored.Id} from={(stored.FromStep is { } f ? f.ToString(CultureInfo.InvariantCulture) : "none")} to={stored.ToStep}"), command.Actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            RampLog.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(await view.ViewAsync(account.Trading.RiskProfileId, account.Trading.OverrideRiskPerTradePct, command.AccountId, cancellationToken));
    }

    private Result<RampView> Refuse(Error error)
    {
        RampLog.Refused(logger, error.Code);
        return Result.Failure<RampView>(error);
    }
}
