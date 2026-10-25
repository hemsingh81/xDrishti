namespace XDrishti.Domain.Reporting;

/// <summary>
/// The owner's confirmation of a charge sheet as their own (ISS-25, P8-00 question 19): which sheet, the hash of its rates at the time, and when. A net report needs one and prints it in its evidence. The hash binds the confirmation to the rates, not to the name: a sheet
/// saved again under the same id with other rates is not what the owner confirmed. <b>P8-06 fills this only from the stored owner confirmation, never from a field of a request</b>: a caller must not be able to make a report net by saying so.
/// </summary>
/// <param name="SheetId">The identifier of the confirmed sheet.</param>
/// <param name="RatesSha256">The SHA-256 of the sheet's rates (id and schedules) that were confirmed: 64 lower-case hex characters.</param>
/// <param name="ConfirmedOn">India date of the confirmation.</param>
public sealed record ChargeSheetConfirmation(string SheetId, string RatesSha256, DateOnly ConfirmedOn)
{
    /// <summary>What was confirmed, as printed.</summary>
    public string Text => $"charge sheet '{SheetId}' with rates {(RatesSha256.Length >= 12 ? RatesSha256[..12] : RatesSha256)} confirmed by the owner on {ConfirmedOn:yyyy-MM-dd}";
}
