using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Walks stored history in time order and reports the drift events (P6-09, ADR 0074). The "live" stream is the tail of history from <c>monitorFrom</c> on; the band is made from what was known before it. Every check reads only what was known by its own instant.
/// Pure and deterministic: the input order does not matter. It records nothing; the transitions it lists are suggestions.
/// </summary>
public static class DriftReplay
{
    /// <summary>The failure code for bad options or input.</summary>
    public const string InvalidCode = "drift.invalid";

    /// <summary>Replays the cells (and the calibration outcomes of a model, if given) from <paramref name="monitorFromEpochSeconds"/> on.</summary>
    public static Result<DriftReport> Run(IReadOnlyList<DriftCellInput> cells, long monitorFromEpochSeconds, DriftOptions options, string? calibrationSubject = null, IReadOnlyList<CalibrationObservation>? outcomes = null, string calibrationNote = "")
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Problems();
        if (problems.Count > 0)
        {
            return Fail(string.Join("; ", problems));
        }

        if (cells.Count == 0 && outcomes is null)
        {
            return Fail("no cells were given");
        }

        if (cells.Select(c => c.CellKey).Distinct(StringComparer.Ordinal).Count() != cells.Count)
        {
            return Fail("a cell is given twice");
        }

        if (cells.Any(c => c.Trades.Any(t => !double.IsFinite(t.R))))
        {
            return Fail("a trade has an R that is not a number");
        }

        var results = new List<CellDrift>();
        var proposals = new List<DriftProposal>();
        foreach (var cell in cells.OrderBy(c => c.CellKey, StringComparer.Ordinal))
        {
            var reference = cell.Trades.Where(t => t.KnownAtEpochSeconds < monitorFromEpochSeconds).ToList();
            var monitored = cell.Trades.Count - reference.Count;
            var band = RollingBandDetector.MakeBand(reference, options);
            if (band.IsFailure)
            {
                results.Add(new CellDrift(cell.CellKey, reference.Count, monitored, null, band.Error.Message, []));
                continue;
            }

            var events = RollingBandDetector.Run(cell.CellKey, cell.Trades, monitorFromEpochSeconds, band.Value, options)
                .Concat(CusumDetector.Run(cell.CellKey, cell.Trades, monitorFromEpochSeconds, band.Value, options))
                .OrderBy(e => e.AtEpochSeconds).ThenByDescending(e => e.Fired).ThenBy(e => e.Detector)
                .ToList();
            results.Add(new CellDrift(cell.CellKey, reference.Count, monitored, band.Value, null, events));
            var active = 0;
            foreach (var e in events)
            {
                active += e.Fired ? 1 : -1;
                if (e.Fired && active == 1)
                {
                    proposals.Add(new DriftProposal(cell.CellKey, e.AtEpochSeconds, true, $"{e.Detector}: {e.Evidence}"));
                }
                else if (!e.Fired && active == 0)
                {
                    proposals.Add(new DriftProposal(cell.CellKey, e.AtEpochSeconds, false, $"{e.Detector}: {e.Evidence}"));
                }
            }
        }

        // Fires are read before clears at one instant, so a hand-over between monitors is not a recovery followed by a pause.
        proposals = [.. proposals.OrderBy(p => p.AtEpochSeconds).ThenBy(p => p.CellKey, StringComparer.Ordinal)];

        CalibrationDrift? calibration = null;
        if (outcomes is not null)
        {
            var subject = string.IsNullOrWhiteSpace(calibrationSubject) ? "model" : calibrationSubject;
            var run = CalibrationDriftDetector.Run(subject, outcomes, options);
            if (run.IsFailure)
            {
                return Result.Failure<DriftReport>(run.Error);
            }

            calibration = new CalibrationDrift(subject, outcomes.Count, run.Value, calibrationNote);
        }

        var c = CultureInfo.InvariantCulture;
        var monitoredCells = results.Count(r => r.Band is not null);
        var limits = new List<string>
        {
            "Replayed history: the monitored period is the held-back tail of stored runs with modelled fills, not live trading (live data starts at P10).",
            string.Create(c, $"The band comes from the trades known before the monitored period and does not move; a cell with fewer than {options.MinReferenceTrades} of them is not monitored ({results.Count - monitoredCells} of {results.Count} were not)."),
            "The band treats the mean of a window as normal with the reference spread; R is skewed and fat-tailed, so the false-alarm rate is only approximately the stated level (measured rates are in ADR 0074).",
            "A cell chosen because its backtest looked good tends to do worse afterwards (regression to the mean), and the three consecutive checks share the one error of the reference mean: a band alarm on such a cell can be the backtest being lucky, not the edge decaying.",
            "Several cells and three monitors are watched at once, so some alarms are chance; an alarm is a reason to look, and a proposal is a line in this report: nothing is recorded in the changelog and the cell's state is not read.",
            "Calibration alarms have no cell transition: the fix for a model that drifted is to recalibrate or retrain, which is a decision (P6-10).",
        };
        return Result.Success(new DriftReport(options, monitorFromEpochSeconds, results, calibration, proposals, limits));
    }

    private static Result<DriftReport> Fail(string message) => Result.Failure<DriftReport>(Error.Validation(InvalidCode, message));
}
