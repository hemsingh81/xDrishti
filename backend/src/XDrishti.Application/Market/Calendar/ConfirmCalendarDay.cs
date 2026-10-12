using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>
/// Confirms a proposed day: from now on the date counts as a holiday or special session. A proposal that lies next to a confirmed day (a session seen on a listed holiday) replaces
/// that day when confirmed. Idempotent (a confirmed day without a proposal stays as it is). Audited.
/// <c>data.invalid</c> for a date that cannot be read, <c>data.calendar_day_not_found</c> when the date has no row or its row was dismissed.
/// </summary>
public sealed record ConfirmCalendarDayCommand(string? Date, string Actor) : ICommand;

internal sealed class ConfirmCalendarDayHandler(
    ITradingDayStore store,
    ITradingCalendar calendar,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock) : ICommandHandler<ConfirmCalendarDayCommand, CalendarDayView>
{
    public async ValueTask<Result<CalendarDayView>> HandleAsync(ConfirmCalendarDayCommand command, CancellationToken cancellationToken)
    {
        var date = CalendarChecks.RouteDate(command.Date);
        if (date.IsFailure)
        {
            return Result.Failure<CalendarDayView>(date.Error);
        }

        for (var attempt = 0; attempt < CalendarChecks.Attempts; attempt++)
        {
            var day = await store.FindAsync(date.Value, cancellationToken);
            if (day is null || day.Status == CalendarStatus.Dismissed)
            {
                return Result.Failure<CalendarDayView>(DataErrors.CalendarDayNotFound);
            }

            var now = clock.GetUtcNow();
            if (!(day.HasPendingProposal ? day.ConfirmProposal(now) : day.Confirm(now)))
            {
                return Result.Success(CalendarDayView.From(day));
            }

            events.Record(AccountEvent.Record(AccountEventType.CalendarDayConfirmed, null, AddCalendarDayHandler.Describe(day), command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                calendar.Invalidate();
                return Result.Success(CalendarDayView.From(day));
            }
        }

        return Result.Failure<CalendarDayView>(DataErrors.Invalid("date", "The calendar changed meanwhile. Try again."));
    }
}
