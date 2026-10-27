using System.Globalization;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Market.Context;

/// <summary>The regime label of one session (P6-02): the regime read from the context day before it, or why there is none.</summary>
public sealed record RegimeDayView(DateOnly Date, string? Regime, int? Code, string? Basis, DateOnly? ContextAsOf, string? Unavailable);

/// <summary>How many sessions of the range fall in one regime, by basis; a regime with no session shows 0 (nothing is hidden).</summary>
public sealed record RegimeCountView(string Regime, int Code, int IndexAndVixDays, int IndexOnlyDays);

/// <summary>Sessions with no label, by reason.</summary>
public sealed record RegimeUnavailableView(string Reason, int Days);

/// <summary>
/// The regime labels per session (<c>GET /api/data/regimes</c>, P6-02, ADR 0146): <c>Days</c> for the range asked, the counts per regime and basis and the sessions without a label by reason over the same range, and what the whole stored history makes readable
/// (<c>FirstLabel</c>, <c>FirstIndexAndVix</c>). The label of a session reads only data before it; the index is the regime index of the context (NIFTY 50 by default).
/// </summary>
public sealed record RegimeLabelsView(
    DateOnly From,
    DateOnly To,
    int Sessions,
    IReadOnlyList<RegimeDayView> Days,
    IReadOnlyList<RegimeCountView> Counts,
    IReadOnlyList<RegimeUnavailableView> Unavailable,
    int ContextDays,
    DateOnly? FirstLabel,
    DateOnly? FirstIndexAndVix,
    string Note);

/// <summary>The regime labels of the sessions from <c>from</c> to <c>to</c> (<c>yyyy-MM-dd</c>; default the 90 days up to the last closed session; at most 400 sessions). <c>data.invalid</c> for a date that cannot be read, a wrong order or a longer range.</summary>
public sealed record GetRegimeLabelsQuery(string? From, string? To) : IQuery<Result<RegimeLabelsView>>;

internal sealed class GetRegimeLabelsHandler(IContextFigures figures, IContextDayStore store, ITradingCalendar tradingCalendar, TimeProvider clock) : IQueryHandler<GetRegimeLabelsQuery, Result<RegimeLabelsView>>
{
    public const int MaxSessions = 400;

    public const int DefaultDays = 90;

    public const string NoteText = "read before the day: the label of a session comes from the context day before it; the index is NIFTY 50; derived from 1-minute bars; a session with no label says why";

    public async ValueTask<Result<RegimeLabelsView>> HandleAsync(GetRegimeLabelsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var last = calendar.LastClosedSession(clock.GetUtcNow());
        var errors = new FieldErrors();
        var to = last;
        var from = last.AddDays(-DefaultDays);
        var toGiven = !string.IsNullOrWhiteSpace(query.To);
        var fromGiven = !string.IsNullOrWhiteSpace(query.From);
        if (toGiven && !CalendarChecks.TryDate(query.To, out to))
        {
            errors.Add("to", "Use the date as yyyy-MM-dd.");
        }

        if (fromGiven && !CalendarChecks.TryDate(query.From, out from))
        {
            errors.Add("from", "Use the date as yyyy-MM-dd.");
        }

        if (!errors.HasErrors && !fromGiven && toGiven)
        {
            from = to.AddDays(-DefaultDays);
        }

        if (!errors.HasErrors && to < from)
        {
            errors.Add("to", "The end is before the start.");
        }

        var dates = new List<DateOnly>();
        if (!errors.HasErrors)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (calendar.IsTradingDay(d))
                {
                    dates.Add(d);
                }
            }

            if (dates.Count > MaxSessions)
            {
                errors.Add("to", string.Create(CultureInfo.InvariantCulture, $"Ask for at most {MaxSessions} sessions."));
            }
        }

        if (errors.HasErrors)
        {
            return Result.Failure<RegimeLabelsView>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."));
        }

        var labels = await RegimeTagsOf(dates, cancellationToken);
        var days = labels.Select(l => new RegimeDayView(l.Date, l.Name, l.Code, l.Basis, l.ContextAsOf, l.Unavailable)).ToList();
        var counts = RegimeNames.All.Select((name, i) => new RegimeCountView(
            name,
            i + 1,
            labels.Count(l => l.Code == i + 1 && l.Basis == ContextBasis.IndexAndVix),
            labels.Count(l => l.Code == i + 1 && l.Basis == ContextBasis.IndexOnly))).ToList();
        var gone = labels.Where(l => !l.IsAvailable).GroupBy(l => l.Unavailable!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new RegimeUnavailableView(g.Key, g.Count())).ToList();
        var coverage = ContextCoverage.Of(await store.ListAsync(cancellationToken));
        return Result.Success(new RegimeLabelsView(from, to, dates.Count, days, counts, gone, coverage.Days, coverage.RegimeReadableFrom, coverage.RegimeWithVixReadableFrom, NoteText));
    }

    private async ValueTask<List<RegimeLabel>> RegimeTagsOf(List<DateOnly> dates, CancellationToken cancellationToken)
    {
        var reads = await figures.AsOfManyAsync(dates, cancellationToken);
        return [.. dates.Select(d => RegimeLabel.Of(d, reads.TryGetValue(d, out var read) ? read : ContextRead.Not(ContextRead.NoContext)))];
    }
}
