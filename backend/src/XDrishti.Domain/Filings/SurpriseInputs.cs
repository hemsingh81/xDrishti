using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The surprise numbers a run that reads <c>results_sue_q</c> or <c>results_car3_q</c> needs (P15-09, pure, immutable): the observations of every instrument of the run, the lag, and the breakpoints of the <b>previous complete season</b> as they stand at a date. The season of a filing
/// is the calendar quarter of its period end; the breakpoints at a date are the quantiles of the filings of the quarter before it whose reaction session lies strictly before that date, so nothing broadcast after the date, of any season, can change a bucket at it. Taken once per run;
/// <see cref="Sha256"/> goes into the run key.
/// </summary>
public sealed class SurpriseInputs
{
    /// <summary>The version of the breakpoint rules, part of the run key of a run that reads them.</summary>
    public const string Version = "sbp-1";

    private readonly Dictionary<(Guid, int), SurpriseObservation> _byFiling;
    private readonly Dictionary<int, Season> _seasons;
    private readonly ConcurrentDictionary<(int Quarter, int Visible), (double[] Sue, double[] Car3)> _cache = new();

    private sealed record Season(DateOnly[] Reaction, double[] Sue, double[] Car3);

    private SurpriseInputs(Dictionary<(Guid, int), SurpriseObservation> byFiling, Dictionary<int, Season> seasons, int rows, int unverified, string sha256)
    {
        UnverifiedRows = unverified;
        _byFiling = byFiling;
        _seasons = seasons;
        Rows = rows;
        Sha256 = sha256;
    }

    /// <summary>The seasons (calendar quarters of the period end) with at least one observation.</summary>
    public int Seasons => _seasons.Count;

    /// <summary>The seasons that, even complete, have fewer than <see cref="SurpriseBreakpoints.MinimumValues"/> finite values of either measure: their breakpoints are NaN and nothing is ranked against them.</summary>
    public int ThinSeasons => _seasons.Values.Count(s => s.Sue.Count(double.IsFinite) < SurpriseBreakpoints.MinimumValues || s.Car3.Count(double.IsFinite) < SurpriseBreakpoints.MinimumValues);

    /// <summary>The observations.</summary>
    public int Rows { get; }

    /// <summary>The observations whose <c>sue_yoy</c> was built on facts of the synthetic (unverified) parser.</summary>
    public int UnverifiedRows { get; }

    /// <summary>Lower-case hex SHA-256 of the canonical observations (instrument, period, broadcast to the microsecond, both numbers; sorted).</summary>
    public string Sha256 { get; }

    /// <summary>The quarter number of a date: calendar quarters counted from year 0, so the previous quarter is one less.</summary>
    public static int QuarterOf(DateOnly date) => (date.Year * 4) + ((date.Month - 1) / 3);

    /// <summary>Builds the inputs; an observation whose reaction session the calendar cannot find is kept in the hash but ranks nothing.</summary>
    public static SurpriseInputs Create(IEnumerable<SurpriseObservation> observations, TimeSpan lag, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(calendar);
        var cut = observations
            .Select(o => o with { BroadcastTs = NewFiling.ToMicroseconds(o.BroadcastTs)!.Value })
            .OrderBy(o => o.InstrumentId).ThenBy(o => QuarterOf(o.PeriodEnd)).ThenBy(o => o.BroadcastTs.UtcTicks).ThenBy(o => o.PeriodEnd)
            .GroupBy(o => (o.InstrumentId, QuarterOf(o.PeriodEnd)))
            .Select(g => g.First())   // one filing for each instrument and season, the earliest broadcast: a revision of the same quarter must not count twice in the breakpoints
            .ToList();
        var text = new StringBuilder();
        var byFiling = new Dictionary<(Guid, int), SurpriseObservation>(cut.Count);
        var grouped = new Dictionary<int, List<(DateOnly Reaction, double Sue, double Car3)>>();
        foreach (var o in cut)
        {
            text.Append(o.InstrumentId.ToString("D", CultureInfo.InvariantCulture))
                .Append('|').Append(o.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append('|').Append(o.BroadcastTs.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture))
                .Append('|').Append(CanonicalText.Number(o.Sue)).Append('|').Append(CanonicalText.Number(o.Car3)).Append('|').Append(o.Unverified ? '1' : '0').Append('\n');
            byFiling[(o.InstrumentId, QuarterOf(o.PeriodEnd))] = o;
            if (ReactionSession.Of(o.BroadcastTs, lag, calendar) is { } reaction)
            {
                var quarter = QuarterOf(o.PeriodEnd);
                if (!grouped.TryGetValue(quarter, out var list))
                {
                    grouped[quarter] = list = [];
                }

                list.Add((reaction, o.Sue, o.Car3));
            }
        }

        var seasons = grouped.ToDictionary(
            g => g.Key,
            g =>
            {
                var ordered = g.Value.OrderBy(v => v.Reaction).ToArray();
                return new Season([.. ordered.Select(v => v.Reaction)], [.. ordered.Select(v => v.Sue)], [.. ordered.Select(v => v.Car3)]);
            });
        return new SurpriseInputs(byFiling, seasons, cut.Count, cut.Count(o => o.Unverified), CanonicalText.Sha256Hex(text.ToString()));
    }

    /// <summary>The numbers of the instrument's filing for the season of <paramref name="periodEnd"/> (the earliest broadcast of it; a later revision reads the numbers of the first), or null when none is stored. <paramref name="broadcast"/> is not part of the key.</summary>
    public SurpriseObservation? Find(Guid instrumentId, DateOnly? periodEnd, DateTimeOffset broadcast) =>
        periodEnd is { } end && _byFiling.TryGetValue((instrumentId, QuarterOf(end)), out var found) ? found : null;

    /// <summary>The quintiles (<c>sue_yoy</c>, <c>results_car3</c>) of the filing as they stand at the date: its numbers against the breakpoints of the season before its own, built from the filings whose reaction session is strictly before the date. NaN where unknown.</summary>
    public (double Sue, double Car3) QuintilesAt(SurpriseObservation filing, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(filing);
        var previous = QuarterOf(filing.PeriodEnd) - 1;
        if (!_seasons.TryGetValue(previous, out var season))
        {
            return (double.NaN, double.NaN);
        }

        var visible = CountBefore(season.Reaction, date);
        var (sue, car3) = _cache.GetOrAdd((previous, visible), _ => (SurpriseBreakpoints.Of(season.Sue.Take(visible)), SurpriseBreakpoints.Of(season.Car3.Take(visible))));
        return (SurpriseBreakpoints.QuintileOf(filing.Sue, sue), SurpriseBreakpoints.QuintileOf(filing.Car3, car3));
    }

    private static int CountBefore(DateOnly[] sorted, DateOnly date)
    {
        var low = 0;
        var high = sorted.Length;
        while (low < high)
        {
            var mid = (low + high) >>> 1;
            if (sorted[mid] < date)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
