using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Reporting;

namespace XDrishti.Infrastructure.Reporting;

/// <summary>
/// The owner's stored confirmation of the charge sheet (ISS-25, P8-00 question 19, ADR 0100, ADR 0127): the newest confirm-or-revoke row of <c>workflow.charge_sheet_events</c>, which only the owner's endpoint appends. It is read, never written, here. Fails closed: no row, a revoke,
/// a sheet that is no longer stored, or a stored sheet whose rates hash is no longer the confirmed one (the store overwrites a sheet in place) is "not confirmed", so a report stays gross. A hand-written settings row is not read at all.
/// </summary>
internal sealed class ChargeSheetConfirmationStore(IChargeSheetLog log, ICostSheetStore sheets) : IChargeSheetConfirmationSource
{
    public async ValueTask<ChargeSheetConfirmation?> FindAsync(CancellationToken cancellationToken)
    {
        var newest = (await log.ReadAsync(0, cancellationToken)).Newest;
        if (newest is not { Kind: ChargeSheetActKind.Confirm, SheetId: { } sheetId, RatesSha256: { } rates, ConfirmedOn: { } on })
        {
            return null;
        }

        // The confirmation is of these rates: a sheet saved again under the same id with other rates is not what the owner confirmed.
        var sheet = await sheets.FindAsync(sheetId, cancellationToken);
        var state = ChargeSheetStandings.Evaluate(newest, sheet.IsSuccess ? sheet.Value.RatesSha256 : null);
        return state == ChargeSheetState.Confirmed ? new ChargeSheetConfirmation(sheetId, rates, on) : null;
    }
}
