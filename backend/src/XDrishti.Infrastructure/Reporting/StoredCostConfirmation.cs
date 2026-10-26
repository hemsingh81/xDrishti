using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Reporting;

namespace XDrishti.Infrastructure.Reporting;

/// <summary>
/// The production <see cref="ICostConfirmationFact"/> (spec P8-23, ADR 0127; P10-04 criterion 7): the day the owner confirmed THIS sheet, from the one stored confirmation, or null. A sheet other than the confirmed one, a revoked or stale confirmation and a missing one all read null,
/// so the paper statistics stay <c>not_evaluable: costs not confirmed</c> unless the owner's confirmation stands for that sheet. It lives here, behind the same source the reports use, so no Application type but the pinned readers touches the source (ADR 0100).
/// </summary>
internal sealed class StoredCostConfirmation(IChargeSheetConfirmationSource source) : ICostConfirmationFact
{
    public async ValueTask<DateOnly?> ConfirmedOnAsync(string chargeSheetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chargeSheetId);
        var confirmation = await source.FindAsync(cancellationToken);
        return confirmation is not null && string.Equals(confirmation.SheetId, chargeSheetId, StringComparison.Ordinal) ? confirmation.ConfirmedOn : null;
    }
}
