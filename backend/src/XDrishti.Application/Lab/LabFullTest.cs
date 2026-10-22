using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>What a full test produced: the candidate runs (to be stored whole), the verdict and the counts.</summary>
internal sealed record FullTestOutcome(IReadOnlyList<SimulationRun> Runs, LabVerdictView Verdict, int Instruments, int InstrumentsRun, int Trades);

/// <summary>
/// The full test of a Lab run (spec P5-04, ADR 0060): for each timeframe the strategy declares, one same-entries comparison of the candidate exits over the whole window (P4-05); the trades of each side are split off and the exit of each
/// timeframe/side cell is chosen by the walk-forward of P4-06; the out-of-sample trades of the winners are pooled into the evidence and the evaluator of P5-03 gives the verdict. Nothing is written here; the runner stores the runs and the result.
/// </summary>
internal sealed class LabFullTest(
    IQueryHandler<RunComparisonQuery, Result<ComparisonRun>> comparisons,
    ITradingCalendar calendars,
    ITestLedger ledger,
    LabSettings settings)
{
    /// <summary>Said of every cell and every verdict: what the full-period figures are.</summary>
    public const string ExplorationLabel = "exploration: in-sample on the data shown, not evidence";

    /// <summary>Said in every verdict: why the calibration gate is 0.</summary>
    public const string CalibrationLimit = "calibration gate not applicable: no probability model (given 0)";

    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    public async ValueTask<Result<FullTestOutcome>> RunAsync(LabRun run, StrategyDescription description, SimulationRequest template, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(template);
        var errors = new List<string>();
        var exits = ExitFlags.Parse([.. settings.Exits], null, errors);
        if (exits is null || exits.Candidates.Count < 2)
        {
            return Result.Failure<FullTestOutcome>(Error.Validation("lab.full_exits_invalid", "The candidate exits of the full test are not usable: " + string.Join("; ", errors)));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var sessions = new List<DateOnly>();
        for (var d = run.From; d <= run.To; d = d.AddDays(1))
        {
            if (calendar.IsTradingDay(d))
            {
                sessions.Add(d);
            }
        }

        var timeframes = description.Timeframes.Count == 0 ? [template.EvaluationTimeframe] : description.Timeframes.Select(t => t.ToCode()).ToList();
        var runs = new List<SimulationRun>();
        var cells = new List<(SelectionInput Input, ExitSelection Selection)>();
        var cellViews = new List<(string Timeframe, string Side, ExitSelection Selection, SelectionInput Input)>();
        var fullPeriod = new List<FullPeriodView>();
        var instruments = 0;
        var instrumentsRun = 0;
        var defaultTrades = 0;
        for (var i = 0; i < timeframes.Count; i++)
        {
            var request = template with { EvaluationTimeframe = timeframes[i], Exits = exits };
            var compared = await comparisons.HandleAsync(new RunComparisonQuery(request, progress is null ? null : new Offset(progress, i * run.Universe.Instruments.Count)), cancellationToken);
            if (compared.IsFailure)
            {
                return Result.Failure<FullTestOutcome>(compared.Error);
            }

            var comparison = compared.Value;
            runs.AddRange(comparison.Candidates.Select(c => c.Run));
            instruments = comparison.Candidates[0].Run.Totals.Instruments;
            instrumentsRun = Math.Max(instrumentsRun, comparison.Candidates[0].Run.Totals.InstrumentsRun);
            defaultTrades += comparison.Candidates[0].Run.Trades.Count;
            var costed = comparison.Report.Unit == "RNet";
            var all = new List<(SimTrade Trade, string Candidate)>();
            foreach (var candidate in comparison.Candidates)
            {
                all.AddRange(candidate.Run.Trades.Select(t => (t.Trade, candidate.Id)));
            }

            foreach (var side in all.Select(t => t.Trade.Side).Distinct().Order())
            {
                var sideText = side.ToString().ToUpperInvariant();
                var style = comparison.Report.GateApplied ? "intraday" : "swing";
                var cell = comparison.Candidates[0].Run.Strategy.Id + " " + timeframes[i] + " " + sideText + " " + style + " *";
                var trades = all.Where(t => t.Trade.Side == side)
                    .Select(t => new SelectionTrade(t.Candidate, t.Trade.TradeDate, DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(t.Trade.ExitEpochSeconds).ToOffset(IndiaOffset).DateTime), costed ? t.Trade.Costing!.RNet : t.Trade.RFilled))
                    .ToList();
                var input = new SelectionInput(cell, comparison.Report.Unit, !comparison.Report.GateApplied, comparison.EntriesKey, [.. comparison.Candidates.Select(c => c.Exit)], trades, sessions, new SelectionOptions());
                var selection = ExitSelector.Select(input);
                cells.Add((input, selection));
                cellViews.Add((timeframes[i], sideText, selection, input));
                foreach (var candidate in comparison.Candidates)
                {
                    fullPeriod.Add(FullPeriodOf(cell, candidate.Id, trades.Where(t => t.CandidateId == candidate.Id).Select(t => t.R).ToList()));
                }
            }
        }

        var pool = OosEvidenceBuilder.Pool(cells, 0);
        var variants = MultipleTesting.Effective(await ledger.CountVariantsAsync(run.StrategyId, cancellationToken), settings.MultipleTesting);
        var verdict = VerdictEvaluator.Evaluate(settings.Gates, pool.Evidence, variants, settings.MinFoldsForPass);
        var limits = verdict.Limits.Append(CalibrationLimit).ToList();
        if (cells.Count == 0)
        {
            limits.Add("no trades in any cell");
        }

        var cellList = cellViews.Select(c => CellOf(c.Timeframe, c.Side, c.Selection, c.Input)).ToList();
        var view = new LabVerdictView(
            verdict.Outcome.ToString().ToUpperInvariant(),
            verdict.VariantCount,
            verdict.AdjustmentFactor,
            EvidenceOf(pool.Evidence),
            [.. verdict.Gates.Select(g => new GateView(g.Name, g.Required, g.Adjusted, Finite(g.Actual), g.Passed, g.Reason))],
            limits,
            cellList,
            fullPeriod,
            [
                "Only the out-of-sample trades of the walk-forward winners are judged.",
                "The full-period figures are " + ExplorationLabel + ".",
                "The candidate exits are a fixed list; the strategy's own exit is not a candidate.",
            ]);
        return Result.Success(new FullTestOutcome(runs, view, instruments, instrumentsRun, defaultTrades));
    }

    private static CellView CellOf(string timeframe, string side, ExitSelection selection, SelectionInput input)
    {
        var own = OosEvidenceBuilder.Pool([(input, selection)], 0).Evidence;
        var def = selection.Pooled.FirstOrDefault(p => p.Name == "default");
        return new CellView(
            input.Cell,
            timeframe,
            side,
            selection.Default,
            selection.Chosen,
            selection.NoChoiceReason,
            selection.Stability.Stable,
            selection.Stability.Reason,
            selection.Folds.Count,
            selection.Folds.Count(f => f.Winner is not null && !f.Thin),
            own.OosTrades,
            own.OosTrades == 0 ? null : own.ExpectancyR,
            def?.MeanR);
    }

    private static FullPeriodView FullPeriodOf(string cell, string candidate, List<double> r)
    {
        var gains = r.Where(v => v > 0).Sum();
        var losses = -r.Where(v => v < 0).Sum();
        return new FullPeriodView(cell, candidate, r.Count, r.Count == 0 ? null : r.Average(), losses > 0 ? gains / losses : null, r.Count == 0 ? null : r.Sum(), ExplorationLabel);
    }

    private static EvidenceView EvidenceOf(OosEvidence e) => new(e.OosTrades, Finite(e.ExpectancyR), Finite(e.ProfitFactor), e.Folds, e.PositiveFolds, Finite(e.MaxDrawdownR), e.CalibrationErrorPp, e.Method);

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    /// <summary>The instruments done in the timeframes before this one, added to the count of this one.</summary>
    private sealed class Offset(IProgress<int> inner, int offset) : IProgress<int>
    {
        public void Report(int value) => inner.Report(offset + value);
    }
}
