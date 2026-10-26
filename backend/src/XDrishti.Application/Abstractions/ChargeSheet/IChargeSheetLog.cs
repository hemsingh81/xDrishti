using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Abstractions.ChargeSheet;

/// <summary>What the log says now: whether the capability to confirm is on, the newest confirm-or-revoke row and the latest rows of all kinds, newest first.</summary>
/// <param name="CapabilityOn">The newest capability row is "on". No row means OFF.</param>
/// <param name="Newest">The newest confirm or revoke row, or null when the owner never acted on a confirmation.</param>
/// <param name="History">The latest rows of every kind, newest first (empty when no history was asked for).</param>
public sealed record ChargeSheetLedger(bool CapabilityOn, ChargeSheetEntry? Newest, IReadOnlyList<ChargeSheetEntry> History);

/// <summary>The outcome of appending an act.</summary>
/// <param name="Entry">The row written, or the row that already said the same (when nothing was written); null for a revoke with nothing to revoke and no earlier row.</param>
/// <param name="Created">True when a row was written; false when the log already said what the act says (a repeated confirmation, a second revoke, a capability already in that state).</param>
public sealed record ChargeSheetAppend(ChargeSheetEntry? Entry, bool Created);

/// <summary>
/// The read side of the charge-sheet confirmation log (<c>workflow.charge_sheet_events</c>, spec P8-23, ADR 0127). Reads only; the owner's act is <see cref="IChargeSheetLogWriter"/>. The report engine never reads this port: it reads
/// <c>IChargeSheetConfirmationSource</c>, which applies <see cref="ChargeSheetStandings"/> to it.
/// </summary>
public interface IChargeSheetLog
{
    /// <summary>Reads the ledger with up to <paramref name="historyLimit"/> rows of history (0 reads none).</summary>
    ValueTask<ChargeSheetLedger> ReadAsync(int historyLimit, CancellationToken cancellationToken);
}

/// <summary>
/// The write side (spec P8-23, ADR 0127): appends one row, never updates or deletes one. Under one lock it re-reads the newest rows, so two concurrent acts cannot interleave: a repeated confirmation of the same sheet and rates is not written twice, and a confirmation is refused
/// while the capability is off (the database trigger refuses it as well). Only the owner's endpoint depends on this port (an architecture test).
/// </summary>
public interface IChargeSheetLogWriter
{
    /// <summary>Appends the act. <c>chargesheet.capability_off</c> (Forbidden) for a confirmation while the capability is off.</summary>
    ValueTask<Result<ChargeSheetAppend>> AppendAsync(ChargeSheetAct act, CancellationToken cancellationToken);
}
