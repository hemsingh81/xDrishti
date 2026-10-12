using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>
/// The trading calendar: the confirmed holidays and special sessions between <c>from</c> and <c>to</c> (<c>yyyy-MM-dd</c> India dates; default today and 365 days
/// on; at most 3 660 days), every proposal, and the next holiday. <c>data.invalid</c> for dates that cannot be read or are in the wrong order.
/// </summary>
public sealed record GetCalendarQuery(string? From, string? To) : IQuery<Result<CalendarView>>;

internal sealed class GetCalendarHandler(ITradingDayStore store, ITradingCalendar calendar, TimeProvider clock) : IQueryHandler<GetCalendarQuery, Result<CalendarView>>
{
    private const int DefaultDays = 365;

    public async ValueTask<Result<CalendarView>> HandleAsync(GetCalendarQuery query, CancellationToken cancellationToken)
    {
        var today = IndiaTime.Today(clock.GetUtcNow());
        var errors = new FieldErrors();
        var from = today;
        var to = today.AddDays(DefaultDays);
        var fromGiven = !string.IsNullOrWhiteSpace(query.From);
        var toGiven = !string.IsNullOrWhiteSpace(query.To);
        if (fromGiven && !CalendarChecks.TryDate(query.From, out from))
        {
            errors.Add("from", "Use the date as yyyy-MM-dd.");
        }

        if (toGiven && !CalendarChecks.TryDate(query.To, out to))
        {
            errors.Add("to", "Use the date as yyyy-MM-dd.");
        }

        if (fromGiven && !toGiven)
        {
            to = from.AddDays(DefaultDays);
        }

        if (!errors.HasErrors && to < from)
        {
            errors.Add("to", "The end is before the start.");
        }
        else if (!errors.HasErrors && to.DayNumber - from.DayNumber > CalendarChecks.MaxSpanDays)
        {
            errors.Add("to", $"Ask for at most {CalendarChecks.MaxSpanDays:N0} days.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<CalendarView>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."));
        }

        var all = await store.ListAsync(cancellationToken);
        var days = all.Where(d => d.IsConfirmed && d.Id >= from && d.Id <= to).Select(CalendarDayView.From).ToList();
        var proposals = all
            .Where(d => d.Status == CalendarStatus.Proposed || d.HasPendingProposal)
            .Select(d => d.Status == CalendarStatus.Proposed ? CalendarDayView.From(d) : CalendarDayView.FromProposal(d))
            .ToList();
        var next = (await calendar.GetAsync(cancellationToken)).NextHoliday(today);
        return Result.Success(new CalendarView(from, to, days, proposals, next is null ? null : CalendarDayView.From(next)));
    }
}
