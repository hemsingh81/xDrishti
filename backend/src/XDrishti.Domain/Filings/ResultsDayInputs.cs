using System.Globalization;
using System.Text;
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

    private ResultsDayInputs(EventCalendarSnapshot? calendar, Dictionary<Guid, IReadOnlyList<ResultsFilingTime>> filings, int filingCount, int lagMinutes, string filingsSha256)
    {
        Calendar = calendar;
        _filings = filings;
        FilingCount = filingCount;
        LagMinutes = lagMinutes;
        FilingsSha256 = filingsSha256;
    }

    public EventCalendarSnapshot? Calendar { get; }

    public int FilingCount { get; }

    public int LagMinutes { get; }

    /// <summary>Lower-case hex SHA-256 of the canonical filing times (instrument, period, broadcast to the microsecond; sorted, no ids).</summary>
    public string FilingsSha256 { get; }

    /// <summary>True when there is no calendar row and no filing: nothing to read.</summary>
    public bool IsEmpty => (Calendar?.RowCount ?? 0) == 0 && FilingCount == 0;

    public static ResultsDayInputs Create(EventCalendarSnapshot? calendar, IEnumerable<ResultsFilingTime> filings, int lagMinutes)
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
        return new ResultsDayInputs(calendar, byInstrument, cut.Count, lagMinutes, CanonicalText.Sha256Hex(text.ToString()));
    }

    public IReadOnlyList<ResultsFilingTime> FilingsOf(Guid instrumentId) => _filings.GetValueOrDefault(instrumentId, NoFilings);

    /// <summary>The per-date series of one instrument over the dates given.</summary>
    public ResultsDaySeries SeriesFor(Guid instrumentId, TradingCalendar calendar, DateOnly from, DateOnly to) =>
        ResultsDaySeries.Build(Calendar?.RowsOf(instrumentId) ?? [], FilingsOf(instrumentId), TimeSpan.FromMinutes(LagMinutes), calendar, from, to);
}
