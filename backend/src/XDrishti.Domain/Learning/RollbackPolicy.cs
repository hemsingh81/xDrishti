using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>How one rollback trigger reads. The default is <see cref="Unavailable"/>, so an unset state fails closed for the gate's other terms and never fires a rollback by accident.</summary>
public enum RollbackTriggerState
{
    /// <summary>The trigger could not be read (no outcomes, no cell that could give a band, bad input). It never fires a rollback, and it is never a pass either.</summary>
    Unavailable = 0,

    /// <summary>The detector was read and is not in alarm.</summary>
    Quiet = 1,

    /// <summary>The detector is in alarm now.</summary>
    Fires = 2,
}

/// <summary>One trade series of a cell the model scores, with the instant from which it is monitored (the trades before it make the frozen band).</summary>
/// <param name="CellKey">The cell.</param>
/// <param name="Trades">The cell's closed trades.</param>
/// <param name="MonitorFromEpochSeconds">The start of the monitored period.</param>
public sealed record RollbackCellSeries(string CellKey, IReadOnlyList<DriftObservation> Trades, long MonitorFromEpochSeconds);

/// <summary>What the rollback triggers read about a champion (CF-10 part 10d-1, spec D8). A null part is not available.</summary>
/// <param name="Calibration">The champion's scored outcomes whose labels are known and that it was not trained on; null when no source exists.</param>
/// <param name="Cells">The trade series of the cells the model scores; null when no source exists.</param>
public sealed record RollbackInputs(IReadOnlyList<CalibrationObservation>? Calibration, IReadOnlyList<RollbackCellSeries>? Cells)
{
    /// <summary>Nothing is available.</summary>
    public static RollbackInputs None { get; } = new(null, null);
}

/// <summary>One trigger of the rollback with its reading and the numbers.</summary>
/// <param name="Kind"><see cref="RollbackPolicy.Calibration"/> or <see cref="RollbackPolicy.Band"/>.</param>
/// <param name="State">The reading.</param>
/// <param name="Value">The statistic at the alarm, or null.</param>
/// <param name="Limit">What it was compared with, or null.</param>
/// <param name="Text">The numbers as words (the detector's own evidence when it fired).</param>
/// <param name="Cells">For the band trigger, the cells in alarm; empty otherwise.</param>
public sealed record RollbackTrigger(string Kind, RollbackTriggerState State, double? Value, double? Limit, string Text, IReadOnlyList<string> Cells);

/// <summary>The triggers and the decision derived from them.</summary>
/// <param name="Triggers">Both triggers, calibration then band.</param>
public sealed record RollbackDecision(IReadOnlyList<RollbackTrigger> Triggers)
{
    /// <summary>True when either trigger fires. An unavailable trigger never fires.</summary>
    public bool ShouldRollback => Triggers.Any(t => t.State == RollbackTriggerState.Fires);

    /// <summary>The triggers that fire.</summary>
    public IReadOnlyList<RollbackTrigger> Firing => [.. Triggers.Where(t => t.State == RollbackTriggerState.Fires)];
}

/// <summary>
/// The rollback rule of a champion (CF-10 part 10d-1, spec D8 and AC11, ADR 0220). It does not recompute the P6-09 numbers: the calibration trigger <b>is</b> <see cref="CalibrationDriftDetector"/> (gap above the 5 pp limit and above 2.5 standard errors of the window, on a window of 100,
/// three checks in a row) and the band trigger <b>is</b> <see cref="RollingBandDetector"/> (20-trade means below the frozen band at 3 checks in a row), each read as "in alarm now": the newest event is a fire that has not cleared. Pure and deterministic. A series that cannot be read is
/// <see cref="RollbackTriggerState.Unavailable"/> and fires nothing.
/// </summary>
public static class RollbackPolicy
{
    /// <summary>The P6-09 calibration detector.</summary>
    public const string Calibration = "calibration";

    /// <summary>The P6-09 rolling band detector.</summary>
    public const string Band = "band";

    /// <summary>Reads both triggers.</summary>
    public static RollbackDecision Evaluate(RollbackInputs inputs, DriftOptions options)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);
        return new RollbackDecision([CalibrationTrigger(inputs.Calibration, options), BandTrigger(inputs.Cells, options)]);
    }

    private static RollbackTrigger CalibrationTrigger(IReadOnlyList<CalibrationObservation>? outcomes, DriftOptions options)
    {
        if (outcomes is null)
        {
            return new RollbackTrigger(Calibration, RollbackTriggerState.Unavailable, null, null, "No scored outcomes of the champion are available.", []);
        }

        var run = CalibrationDriftDetector.Run("champion", outcomes, options);
        if (run.IsFailure)
        {
            return new RollbackTrigger(Calibration, RollbackTriggerState.Unavailable, null, null, run.Error.Message, []);
        }

        if (run.Value.Count > 0 && run.Value[^1] is { Fired: true } alarm)
        {
            return new RollbackTrigger(Calibration, RollbackTriggerState.Fires, alarm.Value, alarm.Limit, alarm.Evidence, []);
        }

        return new RollbackTrigger(Calibration, RollbackTriggerState.Quiet, null, null, string.Create(CultureInfo.InvariantCulture, $"The calibration detector is not in alarm ({outcomes.Count} scored outcomes)."), []);
    }

    private static RollbackTrigger BandTrigger(IReadOnlyList<RollbackCellSeries>? cells, DriftOptions options)
    {
        if (cells is null)
        {
            return new RollbackTrigger(Band, RollbackTriggerState.Unavailable, null, null, "No trade series of the cells the champion scores are available.", []);
        }

        var monitored = 0;
        var alarming = new List<(string Cell, DriftEvent Event)>();
        foreach (var cell in cells.OrderBy(c => c.CellKey, StringComparer.Ordinal))
        {
            var reference = cell.Trades.Where(t => t.KnownAtEpochSeconds < cell.MonitorFromEpochSeconds).ToList();
            var band = RollingBandDetector.MakeBand(reference, options);
            if (band.IsFailure)
            {
                continue; // a cell with too few reference trades or no spread is not monitored (P6-09); it is neither in alarm nor a pass
            }

            monitored++;
            var events = RollingBandDetector.Run(cell.CellKey, cell.Trades, cell.MonitorFromEpochSeconds, band.Value, options);
            if (events.Count > 0 && events[^1].Fired)
            {
                alarming.Add((cell.CellKey, events[^1]));
            }
        }

        if (monitored == 0)
        {
            return new RollbackTrigger(Band, RollbackTriggerState.Unavailable, null, null, "No cell the champion scores has a band to judge it by.", []);
        }

        if (alarming.Count > 0)
        {
            var first = alarming[0];
            return new RollbackTrigger(Band, RollbackTriggerState.Fires, first.Event.Value, first.Event.Limit, string.Create(CultureInfo.InvariantCulture, $"{alarming.Count} of {monitored} monitored cell(s) in alarm; first {first.Cell}: {first.Event.Evidence}"), [.. alarming.Select(a => a.Cell)]);
        }

        return new RollbackTrigger(Band, RollbackTriggerState.Quiet, null, null, string.Create(CultureInfo.InvariantCulture, $"None of {monitored} monitored cell(s) is in alarm."), []);
    }
}
