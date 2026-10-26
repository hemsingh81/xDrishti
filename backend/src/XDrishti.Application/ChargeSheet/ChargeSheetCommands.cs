using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.ChargeSheet;

/// <summary>Reads what stands about the confirmation, and a preview of one stored sheet (the one named, else the one in force).</summary>
/// <param name="SheetId">The sheet to show; null for the one in force.</param>
public sealed record GetChargeSheetStatusQuery(string? SheetId) : IQuery<Result<ChargeSheetStatusView>>;

/// <summary>
/// The owner's act on the charge-sheet confirmation (spec P8-23, ADR 0127). The actor must be <c>owner:...</c> and must come from the authenticated identity (the endpoint needs the password again for THIS act), never from a body. There is no flag that means
/// "confirmed": a confirmation names a sheet and the rates hash the owner saw, and the handler compares it with the stored sheet.
/// </summary>
/// <param name="Kind">The act.</param>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="SheetId">The sheet to confirm (only for a confirmation).</param>
/// <param name="RatesSha256">The rates hash shown to the owner (only for a confirmation).</param>
public sealed record RecordChargeSheetActCommand(ChargeSheetActKind Kind, string Actor, string Reason, string? SheetId, string? RatesSha256) : ICommand;

/// <summary>What the act did, and what stands afterwards.</summary>
/// <param name="Changed">True when a row was written; false when the log already said the same.</param>
/// <param name="Status">The status after the act.</param>
public sealed record ChargeSheetActOutcome(bool Changed, ChargeSheetStatusView Status);

/// <summary>What stands about the confirmation now (spec P8-23).</summary>
/// <param name="CapabilityOn">Whether the owner switched the capability to confirm on. OFF by default; nothing confirms while it is off.</param>
/// <param name="State"><c>not_confirmed</c>, <c>confirmed</c>, <c>stale</c> (the stored sheet has other rates now), <c>sheet_missing</c> or <c>revoked</c>.</param>
/// <param name="Text">One sentence of what stands, as the reports will print it.</param>
/// <param name="InForce">The newest confirmation (confirmed, stale or missing), or null.</param>
/// <param name="Sheet">The stored sheet shown, or null.</param>
/// <param name="SheetNote">Why there is no sheet to show, or null.</param>
/// <param name="Meaning">What confirming means, and what it does not.</param>
/// <param name="History">The acts, newest first (the latest 20).</param>
public sealed record ChargeSheetStatusView(bool CapabilityOn, string State, string Text, ChargeSheetInForceView? InForce, ChargeSheetPreviewView? Sheet, string? SheetNote, string Meaning, IReadOnlyList<ChargeSheetHistoryView> History);

/// <summary>The confirmation that is the newest act on the confirmation.</summary>
/// <param name="SheetId">The sheet.</param>
/// <param name="RatesSha256">The hash of its rates as confirmed.</param>
/// <param name="ConfirmedOn">India date of the confirmation.</param>
/// <param name="Actor">Who.</param>
/// <param name="At">When.</param>
public sealed record ChargeSheetInForceView(string SheetId, string RatesSha256, DateOnly ConfirmedOn, string Actor, DateTimeOffset At);

/// <summary>A stored sheet as the owner is asked to confirm it.</summary>
/// <param name="Id">The id.</param>
/// <param name="Label">Its label.</param>
/// <param name="Source">Where its figures came from.</param>
/// <param name="Checked">What it was checked against; null means unchecked.</param>
/// <param name="Schedules">How many dated schedules it has.</param>
/// <param name="FirstEffective">The first schedule's start.</param>
/// <param name="LastEffective">The last schedule's start.</param>
/// <param name="RatesSha256">The SHA-256 of its rates now: what a confirmation must carry.</param>
public sealed record ChargeSheetPreviewView(string Id, string Label, string Source, string? Checked, int Schedules, DateOnly FirstEffective, DateOnly LastEffective, string RatesSha256);

/// <summary>One act in the history.</summary>
/// <param name="Id">The row.</param>
/// <param name="Kind"><c>confirm</c>, <c>revoke</c>, <c>gate_on</c> or <c>gate_off</c>.</param>
/// <param name="SheetId">The sheet, for a confirmation or revoke.</param>
/// <param name="RatesSha256">Its rates hash, for a confirmation or revoke.</param>
/// <param name="Actor">Who.</param>
/// <param name="Reason">Why.</param>
/// <param name="At">When.</param>
public sealed record ChargeSheetHistoryView(long Id, string Kind, string? SheetId, string? RatesSha256, string Actor, string Reason, DateTimeOffset At);
