using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Application.Paper;

/// <summary>The production <see cref="ICostConfirmationFact"/> of this slice: no sheet is confirmed (spec P10-04, criterion 7).</summary>
internal sealed class NotConfirmed : ICostConfirmationFact
{
    public ValueTask<DateOnly?> ConfirmedOnAsync(string chargeSheetId, CancellationToken cancellationToken) => ValueTask.FromResult<DateOnly?>(null);
}
