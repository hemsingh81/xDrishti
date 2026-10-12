using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market.Calendar;

/// <summary>
/// One date that is not an ordinary weekday session (ADR 0017 §1, <c>market.trading_days</c>): a <see cref="CalendarDayKind.Holiday"/> or a
/// <see cref="CalendarDayKind.Special"/> session with its own window. Ordinary weekdays have no row. A row only changes what counts as a session once it is
/// <see cref="CalendarStatus.Confirmed"/>.
/// <para>
/// A confirmed day can carry a <b>proposal</b> next to it (<see cref="ProposalStatus"/>): what the stored bars show when it contradicts the confirmed row (a session on an
/// NSE-listed holiday, say). The proposal changes nothing until the owner confirms it; the confirmed row keeps deciding what a session is meanwhile.
/// </para>
/// </summary>
public sealed class TradingDay : Entity<DateOnly>
{
    public const int MaxNoteLength = 200;

    /// <summary>The earliest date the calendar holds (the data starts well after it).</summary>
    public static readonly DateOnly MinDate = new(2000, 1, 1);

    /// <summary>The last minute-of-day a session window may end at (23:59; the end is exclusive).</summary>
    public const int LastMinute = 23 * 60 + 59;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private TradingDay(
        DateOnly id,
        CalendarDayKind kind,
        TimeOnly? sessionFrom,
        TimeOnly? sessionTo,
        CalendarSource source,
        CalendarStatus status,
        string? note,
        DateTimeOffset updatedAt)
        : base(id)
    {
        Kind = kind;
        SessionFrom = sessionFrom;
        SessionTo = sessionTo;
        Source = source;
        Status = status;
        Note = note;
        UpdatedAt = updatedAt;
    }

    public CalendarDayKind Kind { get; private set; }

    /// <summary>The start of the session, India time (only for a special session).</summary>
    public TimeOnly? SessionFrom { get; private set; }

    /// <summary>The end of the session, India time, exclusive (only for a special session).</summary>
    public TimeOnly? SessionTo { get; private set; }

    public CalendarSource Source { get; private set; }

    public CalendarStatus Status { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsConfirmed => Status == CalendarStatus.Confirmed;

    /// <summary>What the stored bars propose for this date next to the confirmed row (null: nothing). <see cref="CalendarStatus.Dismissed"/> is kept so it is not proposed again.</summary>
    public CalendarStatus? ProposalStatus { get; private set; }

    public CalendarDayKind? ProposedKind { get; private set; }

    public TimeOnly? ProposedFrom { get; private set; }

    public TimeOnly? ProposedTo { get; private set; }

    public string? ProposedNote { get; private set; }

    /// <summary>Whether a proposal waits next to the confirmed row for the owner.</summary>
    public bool HasPendingProposal => ProposalStatus == CalendarStatus.Proposed && ProposedKind is not null;

    /// <summary>The proposed special session's window in minutes since midnight; null for a proposed holiday or without a proposal.</summary>
    public (int From, int To)? ProposedWindow =>
        HasPendingProposal && ProposedKind == CalendarDayKind.Special && ProposedFrom is { } from && ProposedTo is { } to ? (Minute(from), Minute(to)) : null;

    /// <summary>The special session's window in minutes since midnight; null for a holiday.</summary>
    public (int From, int To)? Window =>
        Kind == CalendarDayKind.Special && SessionFrom is { } from && SessionTo is { } to ? (Minute(from), Minute(to)) : null;

    public static TradingDay Holiday(DateOnly date, CalendarSource source, CalendarStatus status, string? note, DateTimeOffset now)
    {
        CheckDate(date);
        return new TradingDay(date, CalendarDayKind.Holiday, null, null, source, status, Clean(note), now);
    }

    public static TradingDay Special(DateOnly date, int fromMinute, int toMinute, CalendarSource source, CalendarStatus status, string? note, DateTimeOffset now)
    {
        CheckDate(date);
        CheckWindow(fromMinute, toMinute);
        return new TradingDay(date, CalendarDayKind.Special, AsTime(fromMinute), AsTime(toMinute), source, status, Clean(note), now);
    }

    /// <summary>Whether a window is usable: it starts before it ends and both lie within the day (the end is exclusive, so 23:59 is the latest).</summary>
    public static bool IsValidWindow(int fromMinute, int toMinute) => fromMinute >= 0 && fromMinute < toMinute && toMinute <= LastMinute;

    /// <summary>Makes this day a holiday (<paramref name="window"/> null) or a special session, from the given source and status. Returns false when nothing changed.</summary>
    public bool Replace(CalendarDayKind kind, (int From, int To)? window, CalendarSource source, CalendarStatus status, string? note, DateTimeOffset now)
    {
        var cleaned = Clean(note);
        TimeOnly? from = null;
        TimeOnly? to = null;
        if (kind == CalendarDayKind.Special)
        {
            if (window is not { } given)
            {
                throw new ArgumentException("A special session needs a window.", nameof(window));
            }

            CheckWindow(given.From, given.To);
            from = AsTime(given.From);
            to = AsTime(given.To);
        }

        if (Kind == kind && SessionFrom == from && SessionTo == to && Source == source && Status == status && Note == cleaned)
        {
            return false;
        }

        Kind = kind;
        SessionFrom = from;
        SessionTo = to;
        Source = source;
        Status = status;
        Note = cleaned;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Puts a proposal next to this confirmed row; the row itself is not changed. Returns false when nothing changed: the proposal was dismissed for this date before, or is
    /// there already.
    /// </summary>
    public bool Propose(CalendarDayKind kind, (int From, int To)? window, string? note, DateTimeOffset now)
    {
        if (ProposalStatus == CalendarStatus.Dismissed)
        {
            return false;
        }

        TimeOnly? from = null;
        TimeOnly? to = null;
        if (kind == CalendarDayKind.Special)
        {
            if (window is not { } given)
            {
                throw new ArgumentException("A special session needs a window.", nameof(window));
            }

            CheckWindow(given.From, given.To);
            from = AsTime(given.From);
            to = AsTime(given.To);
        }

        var cleaned = Clean(note);
        if (ProposalStatus == CalendarStatus.Proposed && ProposedKind == kind && ProposedFrom == from && ProposedTo == to && ProposedNote == cleaned)
        {
            return false;
        }

        ProposalStatus = CalendarStatus.Proposed;
        ProposedKind = kind;
        ProposedFrom = from;
        ProposedTo = to;
        ProposedNote = cleaned;
        UpdatedAt = now;
        return true;
    }

    /// <summary>The owner declines the proposal next to this row: the row stays as it is and the proposal is not made again. Returns false when there is no pending proposal.</summary>
    public bool DismissProposal(DateTimeOffset now)
    {
        if (!HasPendingProposal)
        {
            return false;
        }

        ProposalStatus = CalendarStatus.Dismissed;
        ProposedKind = null;
        ProposedFrom = null;
        ProposedTo = null;
        ProposedNote = null;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Drops the proposal next to this row: the pending one (the bars no longer support it), or with <paramref name="includingDismissed"/> also the memory of a dismissed one
    /// (the owner decided the date again). Returns false when there was nothing to drop.
    /// </summary>
    public bool ClearProposal(DateTimeOffset now, bool includingDismissed = false)
    {
        if (ProposalStatus is null || (ProposalStatus == CalendarStatus.Dismissed && !includingDismissed))
        {
            return false;
        }

        ProposalStatus = null;
        ProposedKind = null;
        ProposedFrom = null;
        ProposedTo = null;
        ProposedNote = null;
        UpdatedAt = now;
        return true;
    }

    /// <summary>The pending proposal next to this row becomes the day: the row now has the proposed kind and window, source observed, confirmed. Returns false when there is none.</summary>
    public bool ConfirmProposal(DateTimeOffset now)
    {
        if (!HasPendingProposal || ProposedKind is not { } kind)
        {
            return false;
        }

        var window = ProposedWindow;
        var note = ProposedNote;
        ClearProposal(now);
        Replace(kind, window, CalendarSource.Observed, CalendarStatus.Confirmed, note, now);
        return true;
    }

    /// <summary>A proposal becomes a day that counts. Returns false when it was not a proposal.</summary>
    public bool Confirm(DateTimeOffset now)
    {
        if (Status != CalendarStatus.Proposed)
        {
            return false;
        }

        Status = CalendarStatus.Confirmed;
        UpdatedAt = now;
        return true;
    }

    /// <summary>The owner declines this day: it stops counting and is not proposed or listed again. Returns false when it was dismissed already.</summary>
    public bool Dismiss(DateTimeOffset now)
    {
        if (Status == CalendarStatus.Dismissed)
        {
            return false;
        }

        Status = CalendarStatus.Dismissed;
        UpdatedAt = now;
        return true;
    }

    private static int Minute(TimeOnly time) => time.Hour * 60 + time.Minute;

    private static TimeOnly AsTime(int minute) => new(minute / 60, minute % 60);

    private static void CheckDate(DateOnly date)
    {
        if (date < MinDate)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "The date is before the calendar starts.");
        }
    }

    private static void CheckWindow(int fromMinute, int toMinute)
    {
        if (!IsValidWindow(fromMinute, toMinute))
        {
            throw new ArgumentException("A session starts before it ends and lies within one day.");
        }
    }

    private static string? Clean(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }
}
