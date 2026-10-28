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
/// The owner's act of changing the live switches (spec P11-04, ADR 0148). Asks the live-writes gate again, refuses a non-owner actor and an empty reason, and appends one row if nobody changed the switches meanwhile; the database stamps the hash. One account event
/// <c>LiveSettingsChanged</c> with the two values. The endpoint is responsible for the session and the fresh password. No value allows an order.
/// </summary>
internal sealed class ChangeLiveSettingsHandler(ILiveSettingsStore store, ILiveWritesGate gate, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeLiveSettingsHandler> logger) : ICommandHandler<ChangeLiveSettingsCommand, LiveSettingsView>
{
    public const int MaxReason = 500;

    public async ValueTask<Result<LiveSettingsView>> HandleAsync(ChangeLiveSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!gate.Enabled)
        {
            return Refuse(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."));
        }

        if (command.Actor is not { } actor || !actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length || actor.Length > 100)
        {
            return Refuse(Error.Forbidden(LiveErrors.OwnerOnly, "Only the signed-in owner changes the live switches."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReason)
        {
            return Refuse(RampErrors.Invalid("reason", $"Give a reason of 1 to {MaxReason} characters."));
        }

        var current = await store.CurrentAsync(cancellationToken);
        if (command.ExpectedSettingsId is { } expected && expected != (current.Id ?? 0))
        {
            return Refuse(Error.Conflict(LiveErrors.Stale, "The live switches were changed meanwhile; read them again."));
        }

        var row = await store.AppendAsync(actor, command.AutoApproveTickets, command.AutoRampUp, reason, current.Id, cancellationToken);
        if (row is null)
        {
            return Refuse(Error.Conflict(LiveErrors.Stale, "The live switches were changed meanwhile; read them again."));
        }

        RampLog.SettingsChanged(logger, row.Id, row.AutoApproveTickets, row.AutoRampUp);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.LiveSettingsChanged, null, string.Create(CultureInfo.InvariantCulture, $"live.auto_approve_tickets={(row.AutoApproveTickets ? "on" : "off")} live.auto_ramp_up={(row.AutoRampUp ? "on" : "off")} row={row.Id}"), actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            RampLog.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(GetLiveSettingsHandler.View(new LiveSettings(row.Id, row.Hash, row.AutoApproveTickets, row.AutoRampUp), await store.HistoryAsync(GetLiveSettingsHandler.HistoryLimit, cancellationToken), gate.Enabled));
    }

    private Result<LiveSettingsView> Refuse(Error error)
    {
        RampLog.Refused(logger, error.Code);
        return Result.Failure<LiveSettingsView>(error);
    }
}
