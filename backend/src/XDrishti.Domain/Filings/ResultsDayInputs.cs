using System.Globalization;
using System.Text;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Filings;

/// <summary>
/// What a research run that names <c>days_to_results</c> or <c>days_since_results</c> reads (P15-05): the results-calendar snapshot of the run (or none), the broadcast times of the stored results filings (envelope facts only), the lag, and one hash of the canonical text of the filings
/// so the same strategy over other filings is another run. Taken once before the walk and immutable; <see cref="SeriesFor"/> builds the per-date series of one instrument. A run with no calendar row and no filing is refused by the caller (<see cref="IsEmpty"/>).
/// </summary>
public sealed class ResultsDayInputs
{
    private static readonly IReadOnlyList<ResultsFilingTime> NoFilings = [];

    private readonly Dictionary<Guid, IReadOnlyList<ResultsFilingTime>> _filings;

    private ResultsDayInputs(EventCalendarSnapshot? calendar, Dictionary<Guid, IReadOnlyList<ResultsFilingTime>> filings, int filingCount, int lagMinutes, string filingsSha256, SurpriseInputs? surprises)
    {
        Surprises = surprises;
        Calendar = calendar;
        _filings = filings;
        FilingCount = filingCount;
        LagMinutes = lagMinutes;
        FilingsSha256 = filingsSha256;
    }

    public EventCalendarSnapshot? Calendar { get; }

    /// <summary>The surprise numbers of the run, or null when the strategy reads no quintile (P15-09).</summary>
    public SurpriseInputs? Surprises { get; }

    /// <summary>Whether the surprise numbers were given (a run that reads a quintile needs them).</summary>
    public bool HasSurprises => Surprises is not null;

    public int FilingCount { get; }

    public int LagMinutes { get; }

    /// <summary>Lower-case hex SHA-256 of the canonical filing times (instrument, period, broadcast to the microsecond; sorted, no ids).</summary>
    public string FilingsSha256 { get; }

    /// <summary>True when there is no calendar row and no filing: nothing to read.</summary>
    public bool IsEmpty => (Calendar?.RowCount ?? 0) == 0 && FilingCount == 0;

    public static ResultsDayInputs Create(EventCalendarSnapshot? calendar, IEnumerable<ResultsFilingTime> filings, int lagMinutes, SurpriseInputs? surprises = null)
    {
        ArgumentNullException.ThrowIfNull(filings);
        var cut = filings
            .Select(f => f with { BroadcastTs = NewFiling.ToMicroseconds(f.BroadcastTs)!.Value })
            .Distinct()
            .OrderBy(f => f.InstrumentId).ThenBy(f => f.PeriodEnd).ThenBy(f => f.BroadcastTs.UtcTicks)
            .ToList();
        var text = new StringBuilder();
        foreach (var f in cut)
        {
            text.Append(f.InstrumentId.ToString("D", CultureInfo.InvariantCulture))
                .Append('|').Append(f.PeriodEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-")
                .Append('|').Append(f.BroadcastTs.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        var byInstrument = cut.GroupBy(f => f.InstrumentId).ToDictionary(g => g.Key, g => (IReadOnlyList<ResultsFilingTime>)g.ToList().AsReadOnly());
        return new ResultsDayInputs(calendar, byInstrument, cut.Count, lagMinutes, CanonicalText.Sha256Hex(text.ToString()), surprises);
    }

    /// <summary>
    /// Of the filings that name a period and whose reaction session lies in the window, how many were broadcast (plus the lag) after 00:00 India time of that session, and how many there are. For those the series never shows <c>days_since_results == 0</c> on the reaction session
    /// (the filing is first visible the next date, where the count is already 1), so a rule that enters on 0 cannot take them (P15-09, the pre-open reporters).
    /// </summary>
    public (int Missed, int Total) CountMissedReactionDays(TradingCalendar calendar, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var lag = TimeSpan.FromMinutes(LagMinutes);
        int missed = 0, total = 0;
        foreach (var filing in _filings.Values.SelectMany(f => f).Where(f => f.PeriodEnd is not null))
        {
            if (ReactionSession.Of(filing.BroadcastTs, lag, calendar) is not { } reaction || reaction < from || reaction > to)
            {
                continue;
            }

            total++;
            if (filing.BroadcastTs + lag > new DateTimeOffset(reaction.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset))
            {
                missed++;
            }
        }

        return (missed, total);
    }

    public IReadOnlyList<ResultsFilingTime> FilingsOf(Guid instrumentId) => _filings.GetValueOrDefault(instrumentId, NoFilings);

    /// <summary>The per-date series of one instrument over the dates given.</summary>
    public ResultsDaySeries SeriesFor(Guid instrumentId, TradingCalendar calendar, DateOnly from, DateOnly to) =>
        ResultsDaySeries.Build(Calendar?.RowsOf(instrumentId) ?? [], FilingsOf(instrumentId), TimeSpan.FromMinutes(LagMinutes), calendar, from, to, Surprises);
}
