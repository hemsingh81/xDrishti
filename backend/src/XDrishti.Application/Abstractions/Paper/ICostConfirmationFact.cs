namespace XDrishti.Application.Abstractions.Paper;

/// <summary>
/// The one fact the paper comparison needs about costs (spec P10-04, criterion 7): the day the owner confirmed a charge sheet as theirs, or null. The only implementation in this slice says it never was, so the expectancy and drawdown
/// statistics read <c>not_evaluable: costs not confirmed</c> until the slice that writes the confirmation supplies the real fact. Paper code never reads the confirmation source itself (ADR 0100).
/// </summary>
public interface ICostConfirmationFact
{
    /// <summary>The day the sheet was confirmed, or null when it was not.</summary>
    ValueTask<DateOnly?> ConfirmedOnAsync(string chargeSheetId, CancellationToken cancellationToken);
}
