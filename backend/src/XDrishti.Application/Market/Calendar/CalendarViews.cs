using System.Globalization;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>
/// A date of the trading calendar (ADR 0017 §1). <c>kind</c>: holiday · special. A special session has <c>sessionFrom</c> and <c>sessionTo</c> (<c>HH:mm</c>, India time,
/// the end exclusive) and <c>minutes</c> (its length, the 1-minute bars a complete session has); a holiday has none of the three. <c>source</c>: manual · observed · nse.
/// <c>status</c>: confirmed · proposed (<c>dismissed</c> days are not listed). <c>replaces</c>: set only on a proposal that lies next to a confirmed day (a session seen on a listed
/// holiday, say) and names that day's kind: the confirmed day stays in force until the owner confirms the proposal (which replaces it) or dismisses it.
/// </summary>
public sealed record CalendarDayView(
    DateOnly Date,
    string Kind,
    string? SessionFrom,
    string? SessionTo,
    int? Minutes,
    string Source,
    string Status,
    string? Note,
    DateTimeOffset UpdatedAt,
    string? Replaces = null)
{
    public static CalendarDayView From(TradingDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        var window = day.Window;
        return new CalendarDayView(
            day.Id,
            day.Kind.ToCode(),
            window is { } w ? Clock(w.From) : null,
            window is { } v ? Clock(v.To) : null,
            window is { } m ? m.To - m.From : null,
            day.Source.ToCode(),
            day.Status.ToCode(),
            day.Note,
            day.UpdatedAt);
    }

    /// <summary>The proposal next to a confirmed day (source observed, status proposed); <see cref="Replaces"/> is the kind of the confirmed day.</summary>
    public static CalendarDayView FromProposal(TradingDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        var window = day.ProposedWindow;
        return new CalendarDayView(
            day.Id,
            (day.ProposedKind ?? day.Kind).ToCode(),
            window is { } w ? Clock(w.From) : null,
            window is { } v ? Clock(v.To) : null,
            window is { } m ? m.To - m.From : null,
            CalendarSource.Observed.ToCode(),
            CalendarStatus.Proposed.ToCode(),
            day.ProposedNote,
            day.UpdatedAt,
            day.Kind.ToCode());
    }

    private static string Clock(int minute) => string.Create(CultureInfo.InvariantCulture, $"{minute / 60:00}:{minute % 60:00}");
}

/// <summary>
/// The trading calendar. <c>days</c>: the confirmed holidays and special sessions from <c>from</c> to <c>to</c> (India dates, both included; default today and a
/// year on), by date. <c>proposals</c>: every proposed day, whatever its date, waiting for Confirm or Dismiss (including proposals that lie next to a confirmed day, marked <c>replaces</c>). <c>nextHoliday</c>: the first confirmed holiday
/// today or later (null when none is known).
/// </summary>
public sealed record CalendarView(DateOnly From, DateOnly To, IReadOnlyList<CalendarDayView> Days, IReadOnlyList<CalendarDayView> Proposals, CalendarDayView? NextHoliday);

/// <summary>
/// Adds a day to the calendar, confirmed (source manual), replacing what the date had before. <c>date</c> is <c>yyyy-MM-dd</c>; <c>kind</c> is holiday (a weekday,
/// no session window) or special (<c>sessionFrom</c> and <c>sessionTo</c> as <c>HH:mm</c> India time, from before to; both left out means the regular 09:15 to 15:30);
/// <c>note</c> is free text up to 200 characters.
/// </summary>
public sealed record AddCalendarDayRequest(string? Date, string? Kind, string? SessionFrom = null, string? SessionTo = null, string? Note = null);

/// <summary>
/// The answer of a refresh from NSE's public holiday list. It never fails the request: <c>succeeded</c> false means the list could not be fetched or read (<c>message</c> says why,
/// nothing changed). Otherwise the counts of the list's holidays that were added, updated, already known and left out (weekends, dates out of range, days the owner
/// decided or the stored bars contradict).
/// </summary>
public sealed record CalendarRefreshResult(bool Succeeded, string Message, int Added, int Updated, int Unchanged, int Skipped, DateTimeOffset At);
