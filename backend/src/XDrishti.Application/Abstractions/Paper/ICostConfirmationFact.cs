namespace XDrishti.Application.Abstractions.Paper;

/// <summary>
/// The one fact the paper comparison needs about costs (spec P10-04, criterion 7): the day the owner confirmed a charge sheet as theirs, or null. Production implements it over the owner's stored confirmation (<c>StoredCostConfirmation</c>, P8-23, ADR 0127): the day, only while the confirmation stands for that sheet. Without
/// one, or after a revoke or a change of the sheet's rates, the expectancy and drawdown statistics read <c>not_evaluable: costs not confirmed</c>. <c>NotConfirmed</c> is the default of the Application and of the tests. Paper code never reads the confirmation source itself (ADR 0100).
/// </summary>
public interface ICostConfirmationFact
{
    /// <summary>The day the sheet was confirmed, or null when it was not.</summary>
    ValueTask<DateOnly?> ConfirmedOnAsync(string chargeSheetId, CancellationToken cancellationToken);
}
