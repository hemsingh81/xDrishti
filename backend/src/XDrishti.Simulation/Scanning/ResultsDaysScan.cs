using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The per-date series of <c>days_to_results</c> and <c>days_since_results</c> for one instrument (P15-05, ADR 0185), opaque outside the scanning stage like <see cref="ScanContext"/>: the running stage builds it with <see cref="For"/> from the run's inputs and hands it to
/// <see cref="EntryScanner"/> without naming the indicator library. The walk does no I/O: the series is built from the snapshot and the filing times already in memory, once per instrument and run.
/// </summary>
public sealed class ResultsDaysScan
{
    private ResultsDaysScan(IReadOnlyDictionary<DateOnly, ResultsDayValues> days)
    {
        Days = days;
    }

    internal IReadOnlyDictionary<DateOnly, ResultsDayValues> Days { get; }

    /// <summary>
    /// Whether the side reads the series: it names one of the four results-day features (<c>days_to_results</c>, <c>days_since_results</c> and, from P15-09, <c>results_sue_q</c> and <c>results_car3_q</c>) or lists the <c>event_exit</c> overlay, which reads <c>days_to_results</c> at
    /// the close of each session it holds a position (asked of the compiled side, so there is one answer for the compiler, the simulator and the scanner).
    /// </summary>
    public static bool Reads(CompiledSide side)
    {
        ArgumentNullException.ThrowIfNull(side);
        return side.Features.Any(f => f.Name is "days_to_results" or "days_since_results" or "results_sue_q" or "results_car3_q") || side.Overlays.Any(o => o.Kind == OverlayKind.EventExit);
    }

    /// <summary>Whether the side reads a quintile feature (P15-09): the run then needs the surprise inputs.</summary>
    public static bool ReadsSurprise(CompiledSide side)
    {
        ArgumentNullException.ThrowIfNull(side);
        return side.Features.Any(f => f.Name is "results_sue_q" or "results_car3_q");
    }

    /// <summary><c>days_to_results</c> as known at the start of the date, or NaN when the series has no row for it or no meeting is visible.</summary>
    internal double DaysToResultsAt(DateOnly date) => Days.TryGetValue(date, out var values) ? values.DaysToResults : double.NaN;

    /// <summary>The series of the instrument over the dates its bars cover. Empty bars give an empty series.</summary>
    public static ResultsDaysScan For(ResultsDayInputs inputs, Guid instrumentId, IReadOnlyList<Bar> bars, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(calendar);
        if (bars.Count == 0)
        {
            return new ResultsDaysScan(new Dictionary<DateOnly, ResultsDayValues>());
        }

        var series = inputs.SeriesFor(instrumentId, calendar, SessionClock.DateOf(bars[0].EpochSeconds), SessionClock.DateOf(bars[^1].EpochSeconds));
        var map = new Dictionary<DateOnly, ResultsDayValues>(series.Points.Count);
        foreach (var (date, point) in series.Points)
        {
            map[date] = new ResultsDayValues(point.DaysToResults, point.DaysSinceResults, point.SueQuintile, point.Car3Quintile);
        }

        return new ResultsDaysScan(map);
    }
}
