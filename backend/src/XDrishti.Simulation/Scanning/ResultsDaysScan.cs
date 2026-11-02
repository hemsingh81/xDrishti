using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;

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

    /// <summary>Whether the side reads either feature (asked of the features the frame will compute, so there is one answer for the compiler, the simulator and the scanner).</summary>
    public static bool Reads(CompiledSide side)
    {
        ArgumentNullException.ThrowIfNull(side);
        return side.Features.Any(f => f.Name is "days_to_results" or "days_since_results");
    }

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
            map[date] = new ResultsDayValues(point.DaysToResults, point.DaysSinceResults);
        }

        return new ResultsDaysScan(map);
    }
}
