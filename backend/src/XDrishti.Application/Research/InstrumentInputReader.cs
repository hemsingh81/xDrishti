using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// The one way a run, and the second read of the trade cards, read an instrument and build what the engine takes (spec P3-04d): the instrument row, the stored bars with the bounds of a run, the adjustment factors, the tick, the
/// guard against adjusting CSV prices twice, and the <see cref="InstrumentReplayInput"/>. Sharing it is what lets the cards recompute a trade from exactly the input the run had. Holds no state; the list of bars is the caller's.
/// </summary>
internal sealed class InstrumentInputReader(IBarStore bars, IBarQueries barQueries, IAdjustmentFactors adjustments)
{
    /// <summary>The instrument row: from <paramref name="known"/> when the caller resolved it already (a basket), else from the master; null when there is none.</summary>
    public async ValueTask<BarsInstrument?> FindAsync(Guid id, IReadOnlyDictionary<Guid, BarsInstrument>? known, CancellationToken cancellationToken) =>
        known is not null ? known.GetValueOrDefault(id) : await barQueries.FindInstrumentAsync(id, cancellationToken);

    /// <summary>The stored bars from the first stored bar to the end of <paramref name="to"/>, and the factors of the instrument.</summary>
    public async ValueTask<(IReadOnlyList<Bar> Raw, AdjustmentFactors Factors)> ReadAsync(Guid id, DateOnly to, CancellationToken cancellationToken)
    {
        var raw = await bars.ReadAsync(id, SimulationBounds.From, SimulationBounds.Until(to), cancellationToken);
        var factors = await adjustments.GetAsync(id, cancellationToken);
        return (raw, factors);
    }

    /// <summary>Builds the engine input from what was read; the tick and the CSV guard decide a preset skip (both can apply: the precedence keeps <c>csv_adjusted_twice</c>).</summary>
    public static InstrumentReplayInput Build(
        Guid id,
        BarsInstrument instrument,
        IReadOnlyList<Bar> raw,
        AdjustmentFactors factors,
        bool acknowledgeCsvAdjusted,
        TradingCalendar calendar,
        IReadOnlyDictionary<Guid, DateOnly> csvThrough,
        ReplayOptions options,
        DateOnly from,
        DateOnly to,
        CostContext? costs,
        IReadOnlyList<ContextDayRecord>? contextDays = null,
        string? contextNote = null)
    {
        var tick = TickSpec.FromMasterPaise(instrument.TickSize);
        var twice = !acknowledgeCsvAdjusted && AdjustsCsvTwice(raw, factors, csvThrough, id);
        SkipReason? preset = twice ? SkipReason.CsvAdjustedTwice : tick.IsFailure ? SkipReason.TickSizeInvalid : null;
        var detail = twice
            ? "An applied corporate action falls into the CSV prices, which are already adjusted at source; acknowledge to run it."
                + (tick.IsFailure ? " " + tick.Error.Message : string.Empty)
            : tick.IsFailure ? tick.Error.Message : null;

        // Nothing is scanned under a preset skip, so the record has no tick and the detail says the master's tick is refused too.
        return new InstrumentReplayInput(
            id,
            instrument.Symbol,
            raw,
            factors,
            csvThrough.TryGetValue(id, out var through) ? through : null,
            calendar,
            options,
            from,
            to,
            tick.IsSuccess ? tick.Value : null,
            preset,
            detail,
            costs,
            contextDays,
            contextNote);
    }

    /// <summary>The guard of ADR 0017 §5: an applied action that changes the bars read and falls on or before the date the loaded CSV files reach would adjust their prices a second time.</summary>
    private static bool AdjustsCsvTwice(IReadOnlyList<Bar> raw, AdjustmentFactors factors, IReadOnlyDictionary<Guid, DateOnly> through, Guid id) =>
        raw.Count > 0
        && through.ContainsKey(id)
        && factors.ActionsAfter(SessionClock.DateOf(raw[0].EpochSeconds)).Any(a => CsvAdjustment.AdjustsTwice(a.ExDate, through, id));
}
