using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Application.Ports;

public enum AlarmClearHow
{
    Auto,
    Owner,
}

public sealed record AlarmDraft(string Code, Guid? AccountId, long? IntentId, string Detail, DateTimeOffset At)
{
    public AlarmSeverity Severity => AlarmCodes.All.TryGetValue(Code, out var severity) ? severity : AlarmSeverity.Critical;
}

/// <summary>
/// The insert-only alarm record (P12-04): a raise is one row, a clear is one row. An alarm is open until a clear row exists. Raising a code that is already open for the same account and intent adds nothing (the watchdog ticks every second). A code outside
/// <see cref="AlarmCodes.All"/> is refused by the store as well as by the database.
/// </summary>
public interface IAlarmStore
{
    /// <summary>True when a new row was written, false when the same alarm was already open.</summary>
    Task<bool> RaiseAsync(AlarmDraft draft, CancellationToken cancellationToken);

    /// <summary>Writes a clear row for every open alarm with this code, account and intent; returns how many.</summary>
    Task<int> ClearAsync(string code, Guid? accountId, long? intentId, string by, AlarmClearHow how, DateTimeOffset at, CancellationToken cancellationToken);
}
