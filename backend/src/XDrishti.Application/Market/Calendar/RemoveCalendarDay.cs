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
/// Takes a day off the calendar (this is also "Dismiss" for a proposal). A proposal that lies next to a confirmed day is dismissed on its own first: the confirmed day stays and the
/// proposal is not made again (remove the date again to take the confirmed day off). The owner's own entry is deleted. A proposal from the stored bars or a day from NSE's list is
/// kept as <c>dismissed</c> instead, so it is not proposed or listed again; removing a dismissed day deletes it (and lets it be proposed again). Audited.
/// <c>data.invalid</c> for a date that cannot be read, <c>data.calendar_day_not_found</c> when the date has no row.
/// </summary>
public sealed record RemoveCalendarDayCommand(string? Date, string Actor) : ICommand;

internal sealed class RemoveCalendarDayHandler(
    ITradingDayStore store,
    ITradingCalendar calendar,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock) : ICommandHandler<RemoveCalendarDayCommand>
{
    public async ValueTask<Result> HandleAsync(RemoveCalendarDayCommand command, CancellationToken cancellationToken)
    {
        var date = CalendarChecks.RouteDate(command.Date);
        if (date.IsFailure)
        {
            return Result.Failure(date.Error);
        }

        for (var attempt = 0; attempt < CalendarChecks.Attempts; attempt++)
        {
            var day = await store.FindAsync(date.Value, cancellationToken);
            if (day is null)
            {
                return Result.Failure(DataErrors.CalendarDayNotFound);
            }

            var now = clock.GetUtcNow();
            var described = AddCalendarDayHandler.Describe(day);
            if (day.HasPendingProposal)
            {
                described = $"Proposal for {day.Id:yyyy-MM-dd} dismissed (the {day.Kind.ToCode()} stays)";
                day.DismissProposal(now);
            }
            else if (day.Source == CalendarSource.Manual || day.Status == CalendarStatus.Dismissed)
            {
                store.Remove(day);
            }
            else
            {
                day.Dismiss(now);
            }

            events.Record(AccountEvent.Record(AccountEventType.CalendarDayRemoved, null, described, command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                calendar.Invalidate();
                return Result.Success();
            }
        }

        return Result.Failure(DataErrors.Invalid("date", "The calendar changed meanwhile. Try again."));
    }
}
