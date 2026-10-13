using System.Globalization;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// The trading-day timeline of the Services screen: market hours of the date by the calendar, the review cutoff (09:05), and every enabled service's starts (marks) or up-time (bands) on that date, with the
/// chain a run-after service follows. <c>date</c> is <c>yyyy-MM-dd</c>, India time; without it, today. <c>services.invalid</c> (field date) for an unreadable date.
/// </summary>
public sealed record GetServicesTimelineQuery(string? Date = null) : IQuery<Result<ServicesTimeline>>;

internal sealed class GetServicesTimelineHandler(ServiceSettingsReader settings, ITradingCalendar calendar, TimeProvider clock) : IQueryHandler<GetServicesTimelineQuery, Result<ServicesTimeline>>
{
    /// <summary>The time before which the next day's plan can still be reviewed (design doc 04 §3).</summary>
    public const string ReviewCutoff = "09:05";

    private const int MaxChainDepth = 6;

    public async ValueTask<Result<ServicesTimeline>> HandleAsync(GetServicesTimelineQuery query, CancellationToken cancellationToken)
    {
        DateOnly date;
        if (query.Date is null)
        {
            date = IndiaTime.Today(clock.GetUtcNow());
        }
        else if (!DateOnly.TryParseExact(query.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return Result.Failure<ServicesTimeline>(ServiceErrors.InvalidField("date", "Use the date as yyyy-MM-dd."));
        }

        var all = await settings.ReadAllAsync(cancellationToken);
        var tradingCalendar = await calendar.GetAsync(cancellationToken);
        var entries = new List<TimelineEntry>();
        foreach (var service in all.Where(s => s.Settings.Enabled && s.Settings.Schedule.Type != ScheduleType.After))
        {
            if (EntryOf(service, date, tradingCalendar) is { } entry)
            {
                entries.Add(entry);
            }
        }

        var followers = all.Where(s => s.Settings is { Enabled: true, Schedule.Type: ScheduleType.After }).ToList();
        for (var pass = 0; pass < MaxChainDepth && followers.Count > 0; pass++)
        {
            foreach (var follower in followers.ToList())
            {
                var target = follower.Settings.Schedule.After;
                var predecessor = entries.Find(e => e.ServiceId == target);
                if (predecessor is { Marks.Count: > 0 })
                {
                    var at = predecessor.Marks[^1];
                    entries.Add(new TimelineEntry(follower.Id, follower.Definition.Name, "mark", [at], null, null, target));
                    followers.Remove(follower);
                }
            }
        }

        var session = tradingCalendar.SessionOf(date);
        return Result.Success(new ServicesTimeline(
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            session is not null,
            session is { } open ? Minutes(open.FromMinute) : null,
            session is { } close ? Minutes(close.ToMinute) : null,
            ReviewCutoff,
            [.. entries.OrderBy(e => all.ToList().FindIndex(s => s.Id == e.ServiceId))]));
    }

    private static TimelineEntry? EntryOf(EffectiveService service, DateOnly date, Domain.Market.Calendar.TradingCalendar calendar)
    {
        var (schedule, name) = (service.Settings.Schedule, service.Definition.Name);
        var bands = ScheduleCalculator.BandsOn(schedule, date, calendar);
        if (bands.Count > 0)
        {
            var (from, to) = bands[0];
            return new TimelineEntry(service.Id, name, "band", [], new TimelineBand(Clock(from), IndiaTime.Today(to) > date ? "24:00" : Clock(to)), schedule.EveryMinutes, null);
        }

        var marks = ScheduleCalculator.OccurrencesOn(schedule, date, calendar, service.NotBeforeSessionEnd);
        return marks.Count == 0 ? null : new TimelineEntry(service.Id, name, "mark", [.. marks.Select(m => new TimelineMark(Clock(m), m))], null, null, null);
    }

    private static string Clock(DateTimeOffset instant) => TimeOnly.FromDateTime(IndiaTime.ToIst(instant).DateTime).ToString(ScheduleSpec.TimeFormat, CultureInfo.InvariantCulture);

    private static string Minutes(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}
