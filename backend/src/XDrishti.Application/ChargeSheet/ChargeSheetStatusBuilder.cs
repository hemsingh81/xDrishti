using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;

namespace XDrishti.Application.ChargeSheet;

/// <summary>Builds the status the screen and the verb show: the ledger, the stored sheet that stands behind the newest confirmation and the one the owner asked to look at.</summary>
internal sealed class ChargeSheetStatusBuilder(IChargeSheetLog log, ICostSheetStore sheets)
{
    /// <summary>The most history rows shown.</summary>
    public const int HistoryLimit = 20;

    /// <summary>What confirming means, in the words the screen and the verb print.</summary>
    public const string Meaning =
        "Confirming says that this stored sheet holds the rates of your own broker's charges, checked by you. While the stored sheet keeps exactly these rates, reports and the paper comparison may print figures after costs (net) for runs costed with them. "
        + "It places no order and changes no stored run or number. If the sheet's rates change, the confirmation stops standing and every figure is gross again until you confirm again. Switching confirming off does not revoke a confirmation; only a revoke does. You can revoke it at any time.";

    public async ValueTask<ChargeSheetStatusView> BuildAsync(string? requestedSheetId, CancellationToken cancellationToken)
    {
        var ledger = await log.ReadAsync(HistoryLimit, cancellationToken);
        var newest = ledger.Newest;
        var inForceId = newest?.Kind == ChargeSheetActKind.Confirm ? newest.SheetId : null;
        var showId = requestedSheetId ?? inForceId;

        CostSheet? shown = null;
        string? note = null;
        if (showId is not null)
        {
            var found = await sheets.FindAsync(showId, cancellationToken);
            if (found.IsSuccess)
            {
                shown = found.Value;
            }
            else
            {
                note = $"No stored charge sheet is named '{showId}'.";
            }
        }

        // What stands is judged against the sheet the confirmation names, not the one on show.
        string? storedRates = shown is not null && shown.Id == inForceId ? shown.RatesSha256 : null;
        if (inForceId is not null && storedRates is null)
        {
            var named = await sheets.FindAsync(inForceId, cancellationToken);
            storedRates = named.IsSuccess ? named.Value.RatesSha256 : null;
        }

        var state = ChargeSheetStandings.Evaluate(newest, storedRates);
        return new ChargeSheetStatusView(
            ledger.CapabilityOn,
            ChargeSheetStandings.Word(state),
            TextOf(state, newest),
            newest is { Kind: ChargeSheetActKind.Confirm, ConfirmedOn: { } on } ? new ChargeSheetInForceView(newest.SheetId!, newest.RatesSha256!, on, newest.Actor, newest.At) : null,
            shown is null ? null : Preview(shown),
            note,
            Meaning,
            [.. ledger.History.Select(h => new ChargeSheetHistoryView(h.Id, ChargeSheetActKinds.Word(h.Kind), h.SheetId, h.RatesSha256, h.Actor, h.Reason, h.At))]);
    }

    private static ChargeSheetPreviewView Preview(CostSheet sheet) =>
        new(sheet.Id, sheet.Label, sheet.Source, sheet.Checked, sheet.Schedules.Count, sheet.Schedules[0].EffectiveFrom, sheet.Schedules[^1].EffectiveFrom, sheet.RatesSha256);

    private static string TextOf(ChargeSheetState state, ChargeSheetEntry? newest) => state switch
    {
        ChargeSheetState.Confirmed => $"Confirmed: charge sheet '{newest!.SheetId}' with rates {newest.RatesSha256![..12]} on {newest.ConfirmedOn:yyyy-MM-dd}. Net figures may be printed for runs costed with exactly these rates.",
        ChargeSheetState.Stale => $"Stale: the stored sheet '{newest!.SheetId}' no longer has the rates you confirmed ({newest.RatesSha256![..12]}). Every figure is gross until you confirm it again.",
        ChargeSheetState.SheetMissing => $"The confirmed sheet '{newest!.SheetId}' is no longer stored. Every figure is gross.",
        ChargeSheetState.Revoked => "Revoked: no sheet is confirmed. Every figure is gross.",
        _ => "Not confirmed: no sheet is confirmed. Every figure is gross.",
    };
}
