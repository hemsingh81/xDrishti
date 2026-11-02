using System.Globalization;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Filings;

/// <summary>
/// <c>GET /api/calendar/results</c> (spec P15-04 criterion 9): the results meetings visible at <c>asOf</c> (default now) whose event session lies between <c>from</c> and <c>to</c> (default: the India date of asOf and the 60 days after), at most 120 days, plus the tracked instruments
/// with no visible upcoming meeting (count and the first 300 symbols). A bad range, a bad or offset-less <c>asOf</c> or an unknown parameter is <c>calendar.invalid_query</c>; nothing stored is an empty list. Read-only: no file content, no estimate.
/// </summary>
internal sealed class GetResultsCalendarHandler(IResultsCalendar calendar, IFilingInstruments instruments, TimeProvider clock) : IQueryHandler<GetResultsCalendarQuery, Result<ResultsCalendarView>>
{
    public const string InvalidQueryCode = "calendar.invalid_query";

    public const int MaxRangeDays = 120;

    public const int DefaultDays = 60;

    public const int MaxMeetings = 2000;

    public const int MaxSymbols = 300;

    private static readonly DateTimeOffset EarliestAsOf = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] AsOfFormats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mmK"];

    public async ValueTask<Result<ResultsCalendarView>> HandleAsync(GetResultsCalendarQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.UnknownParameter)
        {
            return Fail("Unknown parameter: use from, to and asOf.");
        }

        var asOf = clock.GetUtcNow();
        if (query.AsOf is not null)
        {
            if (!TryParseAsOf(query.AsOf, out asOf))
            {
                return Fail("asOf must be an instant with an offset, such as 2026-08-07T16:00:00+05:30 or 2026-08-07T10:30:00Z.");
            }

            if (asOf < EarliestAsOf)
            {
                return Fail("asOf must not be before 2000-01-01.");
            }
        }

        var today = IndiaTime.Today(asOf);
        var from = today;
        var to = today.AddDays(DefaultDays - 1);
        if ((query.From is not null && !TryParseDate(query.From, out from)) || (query.To is not null && !TryParseDate(query.To, out to)))
        {
            return Fail("from and to must be dates such as 2026-08-07.");
        }

        if (query.From is not null && query.To is null)
        {
            to = from.AddDays(DefaultDays - 1);
        }
        else if (query.From is null && query.To is not null)
        {
            from = today;
        }

        if (from > to)
        {
            return Fail("from must not be after to.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            return Fail($"The range is at most {MaxRangeDays} days.");
        }

        var tracked = await instruments.TrackedAsync(cancellationToken);
        var symbols = tracked.ToDictionary(i => i.Id, i => i.Symbol);
        var upcoming = await calendar.UpcomingAsync(asOf, cancellationToken);
        var inRange = upcoming.Where(m => symbols.ContainsKey(m.InstrumentId) && m.EventSession >= from && m.EventSession <= to)
            .OrderBy(m => m.EventSession).ThenBy(m => symbols[m.InstrumentId], StringComparer.Ordinal).ThenBy(m => m.PeriodEnd).ToList();
        var with = upcoming.Select(m => m.InstrumentId).ToHashSet();
        var without = tracked.Where(i => !with.Contains(i.Id)).Select(i => i.Symbol).Order(StringComparer.Ordinal).ToList();
        var meetings = inRange.Take(MaxMeetings).Select(m => new ResultsMeetingView(symbols[m.InstrumentId], m.InstrumentId, m.PeriodEnd, m.MeetingDate, m.EventSession, m.Days, m.IntimatedAt, m.Revision)).ToList();
        return Result.Success(new ResultsCalendarView(from, to, asOf, meetings, inRange.Count > MaxMeetings, new InstrumentsWithoutEntryView(without.Count, [.. without.Take(MaxSymbols)])));
    }

    private static Result<ResultsCalendarView> Fail(string message) => Result.Failure<ResultsCalendarView>(Error.Validation(InvalidQueryCode, message));

    private static bool TryParseDate(string text, out DateOnly date) => DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseAsOf(string text, out DateTimeOffset t) =>
        DateTimeOffset.TryParseExact(text.Trim(), AsOfFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && HasOffset(text.Trim());

    private static bool HasOffset(string text) => text.EndsWith('Z') || (text.Length > 6 && (text[^6] is '+' or '-') && text[^3] == ':');
}
