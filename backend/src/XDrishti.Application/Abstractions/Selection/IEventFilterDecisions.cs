namespace XDrishti.Application.Abstractions.Selection;

/// <summary>One stored row of the decision history. <see cref="SettingSnapshot"/> is the JSON of the three settings and, for an automatic row, the verdict state that decided.</summary>
public sealed record EventFilterRow(long Id, bool Enabled, int ResultsWithinDays, string Approval, string DecidedBy, string Actor, string Reason, string SettingSnapshot, string? VerdictRef, DateTimeOffset ChangedAt);

/// <summary>A row to append; the id and the time are the database's.</summary>
public sealed record NewEventFilterRow(bool Enabled, int ResultsWithinDays, string Approval, string DecidedBy, string Actor, string Reason, string SettingSnapshot, string? VerdictRef);

/// <summary>
/// The decision history as the runtime role has it: SELECT and INSERT on <c>filings.event_filter_decisions</c> (nothing else). The database judges every row again (owner or auto shape, reason, range, an automatic row's limits). An insert answers the new id or the database's refusal.
/// </summary>
public interface IEventFilterDecisions : IEventFilterHistory
{
    Task<(long? Id, string? Refused)> AppendAsync(NewEventFilterRow row, CancellationToken cancellationToken);
}
