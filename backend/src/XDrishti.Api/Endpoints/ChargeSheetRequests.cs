namespace XDrishti.Api.Endpoints;

/// <summary>The body of a confirmation of one stored charge sheet. The owner is never part of it: the actor comes from the signed-in session. There is no flag that says "confirmed": the body names the sheet and the rates hash the owner was shown.</summary>
/// <param name="SheetId">The stored sheet the owner confirms.</param>
/// <param name="RatesSha256">The SHA-256 of its rates as the screen showed it; if the stored sheet has other rates now the answer is 409 <c>chargesheet.rates_changed</c>.</param>
/// <param name="Reason">Why, 1 to 500 characters (for example what the sheet was checked against).</param>
/// <param name="Password">The owner's password, re-entered for THIS act and checked on every request.</param>
internal sealed record ConfirmChargeSheetRequest(string? SheetId, string? RatesSha256, string? Reason, string? Password);

/// <summary>The body of a revocation: it withdraws the confirmation in force, whatever sheet that is.</summary>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS act.</param>
internal sealed record RevokeChargeSheetRequest(string? Reason, string? Password);

/// <summary>The body of the switch that allows confirming (off until the owner switches it on).</summary>
/// <param name="On">True to allow the owner to confirm a sheet, false to stop allowing it. Switching it off does not revoke a confirmation.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS act.</param>
internal sealed record ChangeChargeSheetCapabilityRequest(bool On, string? Reason, string? Password);
