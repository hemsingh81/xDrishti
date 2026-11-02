using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;

namespace XDrishti.Application.Selection;

/// <summary>Stable problem codes of the event-risk filter switch (P15-06).</summary>
public static class EventFilterErrors
{
    public const string ReasonInvalid = "selection.reason_invalid";

    public const string InvalidSetting = "selection.invalid_setting";

    public const string OwnerOnly = "selection.owner_only";

    public const string Stale = "selection.stale";
}

/// <summary>One row of the decision history, newest first. <see cref="SettingSnapshot"/> is the JSON of the settings at that row.</summary>
public sealed record EventFilterRowView(long Id, bool Enabled, int ResultsWithinDays, string Approval, string DecidedBy, string Actor, string Reason, string SettingSnapshot, string? VerdictRef, DateTimeOffset ChangedAt);

/// <summary>Why the filter is not acting: <c>switch_off</c>, <c>history_unreadable</c>, <c>calendar_unreadable</c>, <c>no_calendar_rows</c>, or <c>no_entry_for_instruments</c> with the count of tracked instruments that have no visible results entry at all, and <c>no_upcoming_entry_for_instruments</c> with the count of those whose last meeting has passed and whose next is not yet intimated (unavailable too: fail closed, ADR 0182).</summary>
public sealed record EventFilterReasonView(string Code, int? Count);

/// <summary>
/// The effective switch (the newest row; no row, or an unreadable history, is OFF), its settings, the latest 50 rows and the reasons the filter is not acting. <see cref="InstrumentsTracked"/> and <see cref="InstrumentsWithEntry"/> are the calendar coverage now
/// (null when the calendar could not be read). The auto option acts only on a hold-out confirmed KEEP: <see cref="AutoVerdict"/> is <c>unavailable</c>, a verdict word, or <c>KEEP_NOT_CONFIRMED</c> for a development KEEP that the hold-out has not confirmed.
/// </summary>
public sealed record EventFilterView(
    bool Enabled,
    int ResultsWithinDays,
    string Approval,
    string? DecidedBy,
    string AutoVerdict,
    IReadOnlyList<EventFilterRowView> History,
    IReadOnlyList<EventFilterReasonView> NotActingBecause,
    int? InstrumentsTracked,
    int? InstrumentsWithEntry);

/// <summary>The effective switch and its history. Read only.</summary>
public sealed record GetEventFilterQuery : IQuery<EventFilterView>;

/// <summary>
/// The owner's change of the event-risk switch (P15-06). The endpoint checked the session and the CSRF token and built the actor from the session; the fresh password is checked here for EVERY change, ON and OFF, without opening the elevation window.
/// <see cref="ResultsWithinDays"/> and <see cref="Approval"/> left null keep the value in force.
/// </summary>
public sealed record ChangeEventFilterCommand(string Actor, Guid SessionId, ClientInfo Client, bool? Enabled, int? ResultsWithinDays, string? Approval, string? Reason, string? Password) : ICommand;

/// <summary>What one run of the auto rule did: the action taken (<c>none</c>, <c>turn_on</c>, <c>turn_off</c>) and the closed reason.</summary>
public sealed record EventFilterAutoResult(string Action, string Reason);

/// <summary>Runs the auto rule once against the newest verdict (P15-10: the evaluation verb runs it after each new protocol-F verdict). No actor: it is the system, recorded as <c>auto:event-filter</c>.</summary>
public sealed record ApplyEventFilterAutoCommand : ICommand;
