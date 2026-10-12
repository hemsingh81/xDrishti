using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>
/// Best-effort read of NSE's public holiday list into the calendar (ADR 0017 §1). Every failure — NSE refusing the request, a timeout, a changed format — is a
/// result with a message, never an exception, and changes nothing: manual add is the path that always works. Holidays that are new to the calendar are stored confirmed
/// (source nse); see <see cref="CalendarMerge"/> for how they meet the owner's rows and the stored bars.
/// </summary>
internal sealed class NseCalendarRefresher(
    IHolidaySource source,
    ITradingDayStore store,
    ITradingCalendar calendar,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock,
    ILogger<NseCalendarRefresher> logger)
{
    /// <summary>How far ahead NSE's holidays are taken (it publishes the current year).</summary>
    private const int MaxYearsAhead = 3;

    private const string Advice = " Add the holidays by hand instead.";

    /// <summary>Reads the list and applies it. <paramref name="auditActor"/> (the signed-in owner) is recorded in the audit trail when something changed; automatic runs pass null.</summary>
    public async ValueTask<CalendarRefreshResult> RefreshAsync(string? auditActor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var download = await source.OpenHolidaysAsync(cancellationToken);
        if (download.Kind != SourceDownloadKind.Content || download.Content is null)
        {
            return Unavailable(download.Failure ?? "NSE's holiday list could not be fetched.", now);
        }

        var parse = await NseHolidayList.ParseAsync(download.Content, cancellationToken);
        if (parse.Failure is not null)
        {
            return Unavailable(parse.Failure, now);
        }

        var today = IndiaTime.Today(now);
        var usable = parse.Holidays.Where(h => h.Date >= TradingDay.MinDate && h.Date <= today.AddYears(MaxYearsAhead) && h.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        var counts = CalendarMerge.ApplyNse(usable, await store.ListAsync(cancellationToken), store, now);
        var skipped = counts.Skipped + (parse.Holidays.Count - usable.Count) + parse.Unreadable;

        if (counts.Changed)
        {
            if (auditActor is not null)
            {
                events.Record(AccountEvent.Record(AccountEventType.CalendarRefreshedFromNse, null, $"NSE's holiday list: {counts.Added} added, {counts.Updated} updated", auditActor, now));
            }

            if (!await unitOfWork.TrySaveAsync(cancellationToken))
            {
                CalendarLog.SaveCollided(logger);
                return new CalendarRefreshResult(false, "The calendar changed while NSE's list was being applied. Try again.", 0, 0, 0, 0, now);
            }

            calendar.Invalidate();
        }

        CalendarLog.NseApplied(logger, counts.Added, counts.Updated, counts.Unchanged, skipped);
        var message = $"NSE's list has {usable.Count} weekday holidays: {counts.Added} added, {counts.Updated} updated, {counts.Unchanged} already known" + (skipped > 0 ? $", {skipped} left out." : ".");
        return new CalendarRefreshResult(true, message, counts.Added, counts.Updated, counts.Unchanged, skipped, now);
    }

    private CalendarRefreshResult Unavailable(string reason, DateTimeOffset now)
    {
        CalendarLog.NseUnavailable(logger, reason);
        return new CalendarRefreshResult(false, reason + Advice, 0, 0, 0, 0, now);
    }
}
