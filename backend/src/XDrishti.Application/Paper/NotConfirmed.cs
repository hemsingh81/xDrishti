using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Application.Paper;

/// <summary>The default <see cref="ICostConfirmationFact"/> of the Application and of the tests: no sheet is confirmed (spec P10-04, criterion 7). The host replaces it with the stored owner confirmation (<c>StoredCostConfirmation</c>, P8-23).</summary>
internal sealed class NotConfirmed : ICostConfirmationFact
{
    public ValueTask<DateOnly?> ConfirmedOnAsync(string chargeSheetId, CancellationToken cancellationToken) => ValueTask.FromResult<DateOnly?>(null);
}
