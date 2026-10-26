using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.ChargeSheet;

/// <summary>
/// The owner's act on the charge-sheet confirmation (spec P8-23, ADR 0127): the only writer. The domain refuses a non-owner actor and a missing reason before anything is read. A confirmation needs the capability to be ON, a stored sheet of that name and
/// the rates hash the owner was shown to be the stored sheet's hash NOW (else <c>chargesheet.rates_changed</c>: nothing is written, the owner reads the sheet again). Nothing here confirms by itself: no setting, rule or job calls this handler; the endpoint calls it after a fresh password.
/// </summary>
internal sealed class RecordChargeSheetActHandler(IChargeSheetLog log, IChargeSheetLogWriter writer, ICostSheetStore sheets, ChargeSheetStatusBuilder status, ILogger<RecordChargeSheetActHandler> logger)
    : ICommandHandler<RecordChargeSheetActCommand, ChargeSheetActOutcome>
{
    public async ValueTask<Result<ChargeSheetActOutcome>> HandleAsync(RecordChargeSheetActCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var kind = ChargeSheetActKinds.Word(command.Kind);
        var act = ChargeSheetAct.Create(command.Kind, command.Actor, command.Reason, command.SheetId, command.RatesSha256);
        if (act.IsFailure)
        {
            return Refuse(kind, act.Error);
        }

        // These checks give the clean answers early; the writer re-checks the capability under the lock and the database trigger checks it again. Keep both.
        if (command.Kind == ChargeSheetActKind.Confirm)
        {
            var refusal = await CheckConfirmationAsync(act.Value, cancellationToken);
            if (refusal is { } error)
            {
                return Refuse(kind, error);
            }
        }

        var appended = await writer.AppendAsync(act.Value, cancellationToken);
        if (appended.IsFailure)
        {
            return Refuse(kind, appended.Error);
        }

        if (appended.Value is { Created: true, Entry: { } row })
        {
            ChargeSheetLogEvents.Recorded(logger, row.Id, kind);
        }
        else
        {
            ChargeSheetLogEvents.Repeated(logger, kind);
        }

        return Result.Success(new ChargeSheetActOutcome(appended.Value.Created, await status.BuildAsync(command.SheetId, cancellationToken)));
    }

    private async ValueTask<Error?> CheckConfirmationAsync(ChargeSheetAct act, CancellationToken cancellationToken)
    {
        if (!(await log.ReadAsync(0, cancellationToken)).CapabilityOn)
        {
            return CapabilityOff;
        }

        var sheet = await sheets.FindAsync(act.SheetId!, cancellationToken);
        if (sheet.IsFailure)
        {
            return sheet.Error.Kind == ErrorKind.NotFound ? Error.NotFound("chargesheet.sheet_not_found", "No stored charge sheet has that name.") : sheet.Error;
        }

        return string.Equals(sheet.Value.RatesSha256, act.RatesSha256, StringComparison.Ordinal)
            ? null
            : Error.Conflict("chargesheet.rates_changed", "The stored sheet has other rates than the ones you were shown; read it again before confirming.");
    }

    /// <summary>The refusal of a confirmation while the capability is off (the store and the database say the same).</summary>
    public static Error CapabilityOff { get; } = Error.Forbidden("chargesheet.capability_off", "Confirming a charge sheet is switched off. Switch it on first (it is off by default).");

    private Result<ChargeSheetActOutcome> Refuse(string kind, Error error)
    {
        ChargeSheetLogEvents.Refused(logger, kind, error.Code);
        return Result.Failure<ChargeSheetActOutcome>(error);
    }
}
