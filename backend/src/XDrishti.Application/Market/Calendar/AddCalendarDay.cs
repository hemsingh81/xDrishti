using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>The day that was added and whether the date had no row before (otherwise its row was replaced).</summary>
public sealed record AddedCalendarDay(CalendarDayView Day, bool Created);

/// <summary>
/// Adds a holiday or a special session to the calendar as confirmed (source manual), replacing whatever the date had (a proposal, NSE's row, an earlier entry of
/// the owner's). Audited. <c>data.invalid</c> with the field for a date that cannot be read or is out of range (2000-01-01 to five years ahead), an unknown kind, a holiday on a
/// weekend or with a session window, a special session with an unreadable window or one that does not start before it ends, or a note over 200 characters.
/// </summary>
public sealed record AddCalendarDayCommand(AddCalendarDayRequest Request, string Actor) : ICommand;

internal sealed class AddCalendarDayHandler(
    ITradingDayStore store,
    ITradingCalendar calendar,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock) : ICommandHandler<AddCalendarDayCommand, AddedCalendarDay>
{
    public async ValueTask<Result<AddedCalendarDay>> HandleAsync(AddCalendarDayCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var checkedInput = Check(command.Request, IndiaTime.Today(now));
        if (checkedInput.IsFailure)
        {
            return Result.Failure<AddedCalendarDay>(checkedInput.Error);
        }

        var input = checkedInput.Value;
        for (var attempt = 0; attempt < CalendarChecks.Attempts; attempt++)
        {
            var day = await store.FindAsync(input.Date, cancellationToken);
            var created = day is null;
            if (day is null)
            {
                day = input.Kind == CalendarDayKind.Holiday
                    ? TradingDay.Holiday(input.Date, CalendarSource.Manual, CalendarStatus.Confirmed, input.Note, now)
                    : TradingDay.Special(input.Date, input.Window!.Value.From, input.Window.Value.To, CalendarSource.Manual, CalendarStatus.Confirmed, input.Note, now);
                store.Add(day);
            }
            else
            {
                day.Replace(input.Kind, input.Window, CalendarSource.Manual, CalendarStatus.Confirmed, input.Note, now);
                day.ClearProposal(now, includingDismissed: true); // the owner decided the date; nothing is proposed next to it any more
            }

            events.Record(AccountEvent.Record(AccountEventType.CalendarDayAdded, null, Describe(day), command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                calendar.Invalidate();
                return Result.Success(new AddedCalendarDay(CalendarDayView.From(day), created));
            }
        }

        return Result.Failure<AddedCalendarDay>(DataErrors.Invalid("date", "The calendar changed meanwhile. Try again."));
    }

    internal static string Describe(TradingDay day) =>
        day.Window is { } w
            ? $"Special session {day.Id:yyyy-MM-dd} {w.From / 60:00}:{w.From % 60:00}-{w.To / 60:00}:{w.To % 60:00} ({day.Source.ToCode()})"
            : $"Holiday {day.Id:yyyy-MM-dd} ({day.Source.ToCode()})";

    internal static Result<CalendarInput> Check(AddCalendarDayRequest request, DateOnly today)
    {
        var errors = new FieldErrors();
        var date = default(DateOnly);
        if (!CalendarChecks.TryDate(request.Date, out date))
        {
            errors.Add("date", "Use the date as yyyy-MM-dd.");
        }
        else if (date < TradingDay.MinDate || date > today.AddYears(CalendarChecks.MaxYearsAhead))
        {
            errors.Add("date", $"Use a date from {TradingDay.MinDate:yyyy-MM-dd} to {CalendarChecks.MaxYearsAhead} years ahead.");
        }

        var haveKind = CalendarDayKindCodes.TryParse(request.Kind, out var kind);
        if (!haveKind)
        {
            errors.Add("kind", "Use holiday or special.");
        }

        (int From, int To)? window = null;
        if (haveKind && kind == CalendarDayKind.Holiday)
        {
            if (date != default && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                errors.Add("date", "A weekend has no session anyway. Add a special session instead.");
            }

            if (request.SessionFrom is not null || request.SessionTo is not null)
            {
                errors.Add("sessionFrom", "A holiday has no session window.");
            }
        }
        else if (haveKind)
        {
            window = CheckWindow(request, errors);
        }

        var note = request.Note?.Trim();
        if (note is { Length: > TradingDay.MaxNoteLength })
        {
            errors.Add("note", $"Use at most {TradingDay.MaxNoteLength} characters.");
        }
        else if (note is not null && note.Any(char.IsControl))
        {
            errors.Add("note", "Control characters are not allowed.");
        }

        return errors.HasErrors
            ? Result.Failure<CalendarInput>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."))
            : Result.Success(new CalendarInput(date, kind, window, string.IsNullOrEmpty(note) ? null : note));
    }

    private static (int From, int To)? CheckWindow(AddCalendarDayRequest request, FieldErrors errors)
    {
        if (request.SessionFrom is null && request.SessionTo is null)
        {
            return (SessionClock.OpenMinute, SessionClock.CloseMinute);
        }

        var ok = true;
        if (!CalendarChecks.TryClock(request.SessionFrom, out var from))
        {
            errors.Add("sessionFrom", "Use the time as HH:mm (India time).");
            ok = false;
        }

        if (!CalendarChecks.TryClock(request.SessionTo, out var to))
        {
            errors.Add("sessionTo", "Use the time as HH:mm (India time).");
            ok = false;
        }

        if (ok && !TradingDay.IsValidWindow(from, to))
        {
            errors.Add("sessionTo", "The session must end after it starts, on the same day.");
            ok = false;
        }

        return ok ? (from, to) : null;
    }
}

/// <summary>A checked request to add a day.</summary>
internal sealed record CalendarInput(DateOnly Date, CalendarDayKind Kind, (int From, int To)? Window, string? Note);
