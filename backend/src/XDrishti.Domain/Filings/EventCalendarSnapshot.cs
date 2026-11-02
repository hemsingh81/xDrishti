using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The stored results rows a research run may read, taken once before the walk and immutable (P15-06, ADR 0182): only rows of purpose <c>results</c> with a known intimation time, cut to whole microseconds, and the lag of the point-in-time store. The walk asks it
/// <see cref="Answer"/> per decision, which is <see cref="ResultsDays.Compute"/> on the rows of one instrument (a dictionary lookup of the rows, then <c>Compute</c>, which lists the rows, applies the point-in-time rule and builds a dictionary on every call; the cost is per candidate decision and a handful of rows, not per bar): a row intimated after the instant does not exist at it. The snapshot does no I/O and holds no clock; <see cref="Sha256"/> is the hash of its canonical text
/// (instrument, period, meeting date, status, intimation to the microsecond, content hash of the row; sorted, no ids and no ingest times), so the same rows in any order give the same hash.
/// </summary>
public sealed class EventCalendarSnapshot
{
    /// <summary>The version of the event-filter rules that is written in the run record and the run key (<c>evf_version</c>).</summary>
    public const string RuleVersion = "evf-1";

    private static readonly IReadOnlyList<CalendarRow> NoRows = [];

    private readonly Dictionary<Guid, IReadOnlyList<CalendarRow>> _byInstrument;

    private EventCalendarSnapshot(Dictionary<Guid, IReadOnlyList<CalendarRow>> byInstrument, int rowCount, int lagMinutes, string sha256)
    {
        _byInstrument = byInstrument;
        RowCount = rowCount;
        LagMinutes = lagMinutes;
        Sha256 = sha256;
    }

    /// <summary>The lag in minutes between an intimation and the moment it counts as known (<c>Filings:LagMinutes</c>).</summary>
    public int LagMinutes { get; }

    /// <summary>Rows kept.</summary>
    public int RowCount { get; }

    /// <summary>Lower-case hex SHA-256 of the canonical text of the rows.</summary>
    public string Sha256 { get; }

    /// <summary>The instruments that have at least one row.</summary>
    public int InstrumentCount => _byInstrument.Count;

    /// <summary>Builds the snapshot. A row that is not a <c>results</c> meeting, or has no intimation time, is left out (it can never be visible); a row given twice (same filing id) counts once.</summary>
    public static EventCalendarSnapshot Create(IEnumerable<CalendarRow> rows, int lagMinutes)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (lagMinutes is < PointInTimeRule.MinLagMinutes or > PointInTimeRule.MaxLagMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(lagMinutes), lagMinutes, "The lag is " + PointInTimeRule.MinLagMinutes + " to " + PointInTimeRule.MaxLagMinutes + " minutes.");
        }

        var kept = new Dictionary<Guid, CalendarRow>();
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Purpose != MeetingRow.Results || row.Filing.State != BroadcastState.Ok || row.Filing.BroadcastTs is null)
            {
                continue;
            }

            var cut = row with { Filing = row.Filing with { BroadcastTs = NewFiling.ToMicroseconds(row.Filing.BroadcastTs) } };
            kept.TryAdd(cut.Filing.Id, cut);
        }

        var ordered = kept.Values
            .OrderBy(r => r.Filing.InstrumentId)
            .ThenBy(r => r.Filing.PeriodEnd)
            .ThenBy(r => r.Filing.BroadcastTs!.Value.UtcTicks)
            .ThenBy(r => r.MeetingDate)
            .ThenBy(r => r.Status, StringComparer.Ordinal)
            .ThenBy(r => r.Filing.ContentSha256, StringComparer.Ordinal)
            .ToList();
        var text = new StringBuilder();
        foreach (var row in ordered)
        {
            text.Append(row.Filing.InstrumentId.ToString("D", CultureInfo.InvariantCulture))
                .Append('|').Append(row.Filing.PeriodEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-")
                .Append('|').Append(row.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append('|').Append(row.Status)
                .Append('|').Append(row.Filing.BroadcastTs!.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture))
                .Append('|').Append(row.Filing.ContentSha256)
                .Append('\n');
        }

        var byInstrument = ordered.GroupBy(r => r.Filing.InstrumentId).ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarRow>)g.ToList().AsReadOnly());
        return new EventCalendarSnapshot(byInstrument, ordered.Count, lagMinutes, CanonicalText.Sha256Hex(text.ToString()));
    }

    /// <summary>The rows of one instrument (empty when it has none).</summary>
    public IReadOnlyList<CalendarRow> RowsOf(Guid instrumentId) => _byInstrument.GetValueOrDefault(instrumentId, NoRows);

    /// <summary>"How many sessions until the next results event, as known at <paramref name="t"/>" from these rows; <c>unavailable</c> with a closed reason when the instrument has no usable row.</summary>
    public ResultsAnswer Answer(Guid instrumentId, DateTimeOffset t, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        // shortcut: every call recomputes the current rows of the instrument (a few allocations per candidate decision). Fine for the entries of a 5-year, 214-instrument run (thousands of candidates); if a run ever asks per bar or per parameter combination,
        // precompute per instrument the intervals of constant answer (the sorted instants at which a row becomes visible, with the answer from each) and binary-search them.
        return ResultsDays.Compute(RowsOf(instrumentId), t, TimeSpan.FromMinutes(LagMinutes), calendar);
    }
}
