using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The owner's act of writing a compliance record (spec P11-05, ADR 0147). Asks the gate again, refuses a non-owner actor, applies the rules of <see cref="ComplianceRecord"/> and appends one row (never an update); then one account event
/// <c>ComplianceRecorded</c> with the kind, the outcome and the decided date - never the text - in its own unit of work (its failure is logged and does not undo the row). The endpoint is responsible for the session and the fresh password.
/// Calls nothing outside the database: nothing is sent to Dhan or the ISP.
/// </summary>
internal sealed class RecordComplianceHandler(IComplianceStore store, ILiveWritesGate gate, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<RecordComplianceHandler> logger) : ICommandHandler<RecordComplianceCommand, ComplianceRecordView>
{
    public async ValueTask<Result<ComplianceRecordView>> HandleAsync(RecordComplianceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!gate.Enabled)
        {
            LiveLog.ComplianceRefused(logger, LiveErrors.CapabilityOff);
            return Result.Failure<ComplianceRecordView>(Error.Forbidden(LiveErrors.CapabilityOff, "The live writes are switched off in this deployment (Live__Writes__Enabled is false)."));
        }

        if (command.Actor is not { } actor || !actor.StartsWith("owner:", StringComparison.Ordinal) || actor.Length <= "owner:".Length || actor.Length > 100)
        {
            LiveLog.ComplianceRefused(logger, LiveErrors.OwnerOnly);
            return Result.Failure<ComplianceRecordView>(Error.Forbidden(LiveErrors.OwnerOnly, "Only the signed-in owner writes a compliance record."));
        }

        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var created = ComplianceRecord.Create(
            new ComplianceDraft(command.Kind, command.Outcome, command.DecidedOn, command.Summary, command.Sources, command.StaticIpState, command.Isp, command.MonthlyCost, command.IpVersion, command.WhitelistedAt, command.NextReviewOn),
            today);
        if (created.IsFailure)
        {
            LiveLog.ComplianceRefused(logger, created.Error.Code);
            return Result.Failure<ComplianceRecordView>(created.Error);
        }

        var stored = await store.AppendAsync(created.Value, actor, cancellationToken);
        LiveLog.ComplianceRecorded(logger, stored.Id, stored.Record.Kind, stored.Record.Outcome);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.ComplianceRecorded, null, string.Create(CultureInfo.InvariantCulture, $"record={stored.Id} kind={stored.Record.Kind} outcome={stored.Record.Outcome} decided={stored.Record.DecidedOn:yyyy-MM-dd}"), actor, now));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // the row stands; the audit cross-index is a second write. Only the type of the failure is logged.
            LiveLog.ComplianceAuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(GetComplianceHandler.View(stored, today, true));
    }
}
