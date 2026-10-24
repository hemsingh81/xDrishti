namespace XDrishti.Domain.Reporting;

/// <summary>
/// The owner's confirmation of a charge sheet as their own (ISS-25, P8-00 question 19): which sheet and when. A net report needs one and prints it in its evidence. <b>P8-06 must fill this only from the stored owner confirmation, never from a field of a request</b>:
/// a caller must not be able to make a report net by saying so.
/// </summary>
/// <param name="SheetId">The identifier (or content hash) of the confirmed sheet.</param>
/// <param name="ConfirmedOn">India date of the confirmation.</param>
public sealed record ChargeSheetConfirmation(string SheetId, DateOnly ConfirmedOn)
{
    /// <summary>What was confirmed, as printed.</summary>
    public string Text => $"charge sheet '{SheetId}' confirmed by the owner on {ConfirmedOn:yyyy-MM-dd}";
}
