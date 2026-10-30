using XDrishti.Domain.Execution.Limits;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Application.Ports;

/// <summary>A stored limits snapshot: its id, the sheet and when it takes effect.</summary>
public sealed record LimitsRow(long Id, Guid AccountId, ExecutionLimits Limits, DateTimeOffset EffectiveFrom);

/// <summary>The sheet in force for an account at the database clock (<see cref="DbNow"/>), or null when the account has none.</summary>
public sealed record LimitsReading(DateTimeOffset DbNow, LimitsRow? InForce);

/// <summary>The limits record as the execution service reads it (role xd_exec, SELECT only). Throws when it cannot be read; the caller then refuses.</summary>
public interface ILimitsStore
{
    Task<LimitsReading> ReadAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>The newest settings row as the <c>auto_live</c> path reads it; null when it cannot be read (nothing automatic then).</summary>
public interface IAutoLiveSettingsSource
{
    Task<AutoLiveSettings?> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>One action-log row to write. <see cref="Codes"/> lists the refusal codes (empty for an action taken).</summary>
public sealed record ActionLogEntry(
    Guid? AccountId, string Kind, string By, Guid? IntentPublicId, Guid? TicketId, string Text, IReadOnlyList<string> Codes, long? SettingsId, long? LimitsId);

/// <summary>An entry to reserve in the ledger with its action-log row.</summary>
public sealed record LedgerReservation(
    Guid AccountId, DateOnly Session, Guid IntentPublicId, string Symbol, string StrategyHash, string Product, string Side, int Quantity, decimal Notional, decimal RiskAtStop, string Mode, long LimitsId, long SettingsId);

/// <summary>The outcome of a reservation: both ids when written, else the code of the database's refusal (the transaction left nothing).</summary>
public sealed record LedgerResult(long? LedgerId, long? ActionLogId, string? Refused)
{
    public bool Reserved => LedgerId is not null && ActionLogId is not null;
}

/// <summary>
/// The entry ledger (role xd_exec, insert-only): the day's count and notional, whether a strategy version was used live by an account, and the reservation of one entry with its action-log row in ONE transaction, which the trigger
/// <c>execution.check_ledger_entry</c> judges again under an advisory lock per account and day.
/// </summary>
public interface ILedger
{
    /// <summary>Null when it cannot be read.</summary>
    Task<LedgerDay?> DayAsync(Guid accountId, DateOnly session, CancellationToken cancellationToken);

    /// <summary>Null when it cannot be read.</summary>
    Task<bool?> StrategyUsedAsync(Guid accountId, string strategyHash, CancellationToken cancellationToken);

    Task<LedgerResult> ReserveAsync(LedgerReservation entry, ActionLogEntry action, CancellationToken cancellationToken);
}

/// <summary>The action log (role xd_exec, insert-only): every automatic action and every refusal is one row. Answers the row id, or null when it could not be written.</summary>
public interface IActionLog
{
    Task<long?> AppendAsync(ActionLogEntry entry, CancellationToken cancellationToken);
}

/// <summary>
/// The owner's newest session (spec P12-09 criterion 7). The execution role has no grant on <c>auth.sessions</c> and this slice adds none, so the only implementation is the closed default: no answer, which is never attended.
/// </summary>
public interface IOwnerPresence
{
    Task<OwnerSeen?> NewestAsync(CancellationToken cancellationToken);
}

internal sealed class ClosedOwnerPresence : IOwnerPresence
{
    public Task<OwnerSeen?> NewestAsync(CancellationToken cancellationToken) => Task.FromResult<OwnerSeen?>(null);
}

/// <summary>When the app's feed last priced a symbol; null when there is no price or it cannot be read (stale).</summary>
public interface IFeedHealth
{
    Task<DateTimeOffset?> LastPriceAtAsync(string symbol, CancellationToken cancellationToken);
}

/// <summary>The loss cap of an account for P12-05's <c>loss_cap</c> trigger: the cap of the sheet in force and whether the halt exits all. Null when the sheet is incomplete or cannot be read: no number, no cap halt.</summary>
public sealed record LossCapSetting(decimal MaxLossPerDay, bool ExitAll, long LimitsId);

public interface ILossCapSource
{
    Task<LossCapSetting?> ForAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>The one call the automatic path makes after its row is written: hand the reserved entry to the sender. No implementation exists in this slice; nothing composes one.</summary>
public interface IEntryHandOver
{
    Task HandOverAsync(Guid intentPublicId, long ledgerId, CancellationToken cancellationToken);
}

/// <summary>The one call the automatic path makes for a risk-reducing action after its row is written. No implementation exists in this slice; nothing composes one.</summary>
public interface IReduceHandOver
{
    Task HandOverAsync(Guid intentPublicId, string kind, long actionLogId, CancellationToken cancellationToken);
}
