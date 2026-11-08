using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Confidence;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>What a full test produced: the candidate runs (to be stored whole), the verdict and the counts. <paramref name="OosSources"/> is the simulated trade behind each of <paramref name="OosTrades"/>, in the same order (the random-entry null of CF-07 restates them); null when not kept.</summary>
internal sealed record FullTestOutcome(IReadOnlyList<SimulationRun> Runs, LabVerdictView Verdict, int Instruments, int InstrumentsRun, int Trades, IReadOnlyList<SelectionTrade> OosTrades, IReadOnlyList<SimTrade>? OosSources = null);

/// <summary>
/// The full test of a Lab run (spec P5-04, ADR 0060): for each timeframe the strategy declares, one same-entries comparison of the candidate exits over the whole window (P4-05); the trades of each side are split off and the exit of each
/// timeframe/side cell is chosen by the walk-forward of P4-06; the out-of-sample trades of the winners are pooled into the evidence and the evaluator of P5-03 gives the verdict. Nothing is written here; the runner stores the runs and the result.
/// </summary>
internal sealed class LabFullTest(
    IQueryHandler<RunComparisonQuery, Result<ComparisonRun>> comparisons,
    ITradingCalendar calendars,
    ITestLedger ledger,
    LabSettings settings,
    ConfidenceOptions? confidence = null,
    RegistrationOptions? registration = null)
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
        // shortcut: every timeframe's candidate runs (all trades) are held in memory until the whole test ends and they are stored; fine for the Lab's universe and window, stream per timeframe if it grows.
        var runs = new List<SimulationRun>();
        var cells = new List<(SelectionInput Input, ExitSelection Selection)>();
        var cellViews = new List<(string Timeframe, string Side, ExitSelection Selection, SelectionInput Input)>();
        var sources = new Dictionary<SelectionTrade, SimTrade>(ReferenceEqualityComparer.Instance);
        var fullPeriod = new List<FullPeriodView>();
        var instruments = 0;
        var instrumentsRun = 0;
        var defaultTrades = 0;
        long candidatesSeen = 0;
        long tierExcluded = 0;
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
            candidatesSeen += comparison.Candidates[0].Run.Totals.CandidatesInWindow;
            tierExcluded += comparison.Candidates[0].Run.Totals.Fates.TierExcluded;
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
                var trades = new List<SelectionTrade>();
                foreach (var t in all.Where(t => t.Trade.Side == side))
                {
                    var selected = new SelectionTrade(t.Candidate, t.Trade.TradeDate, DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(t.Trade.ExitEpochSeconds).ToOffset(IndiaOffset).DateTime), costed ? t.Trade.Costing!.RNet : t.Trade.RFilled, costed ? t.Trade.RExact : null);
                    trades.Add(selected);
                    sources[selected] = t.Trade;   // by reference: the pooled evidence returns these very instances (CF-07)
                }

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

        var pool = OosEvidenceBuilder.Pool(cells, 0, confidence, settings.Gates);
        // CF-06: the verdict is judged with the ledger total at this moment. Until the owner turns CountInFactor on, that total is the number of distinct hashes it always was, and the honest count is shown beside it
        var parts = await ledger.CountTrialsAsync(run.StrategyId, cancellationToken);
        var honest = registration is { CountInFactor: true };
        var variants = MultipleTesting.Effective(TrialCounter.Judged(parts, honest), settings.MultipleTesting);
        var verdict = VerdictEvaluator.Evaluate(settings.Gates, pool.Evidence, variants, settings.MinFoldsForPass);
        var limits = verdict.Limits.Append(CalibrationLimit).ToList();
        limits.AddRange(verdict.Notes ?? []);
        if (verdict.Dependence is { Mode: not ConfidenceMode.Off })
        {
            limits.Add(DependenceView.LimitsText);
        }
        // CF-01: remarks that never cap the outcome
        var weak = cells.Count(c => (c.Selection.Stability.Stable != true || c.Selection.NoChoiceReason is not null) && OosEvidenceBuilder.Pool([c], 0).Evidence.OosTrades > 0);
        if (weak > 0)
        {
            limits.Add(weak + " cells unstable or without an exit choice contributed");
        }

        if (cells.Count == 0)
        {
            limits.Add("no trades in any cell");
        }

        var exclusion = template.Fills?.ConservativeSlippage == true ? new TierExclusionView(candidatesSeen, tierExcluded) : null;
        if (exclusion is not null)
        {
            limits.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"conservative slippage: {exclusion.TierExcluded} of {exclusion.Candidates} candidates ({exclusion.Share * 100:0.#} %) were left out because the instrument was liquidity tier C or had no usable tier on the entry date; the trades judged are the survivors of that filter, not a clean sample. This goes beyond the tier C rule of the spec and awaits the owner's view."));
        }

        var cellList = cellViews.Select(c => CellOf(c.Timeframe, c.Side, c.Selection, c.Input)).ToList();
        var view = new LabVerdictView(
            verdict.Outcome.ToString().ToUpperInvariant(),
            verdict.VariantCount,
            verdict.AdjustmentFactor,
            EvidenceOf(pool.Evidence, verdict.Dependence),
            [.. verdict.Gates.Select(g => new GateView(g.Name, g.Required, g.Adjusted, Finite(g.Actual), g.Passed, g.Reason))],
            limits,
            cellList,
            fullPeriod,
            [
                "Only the out-of-sample trades of the walk-forward winners are judged.",
                "The full-period figures are " + ExplorationLabel + ".",
                "The candidate exits are a fixed list; the strategy's own exit is not a candidate.",
            ],
            Trials: TrialsView.Of(parts, honest, settings.MultipleTesting),
            TierExclusion: exclusion);
        var oos = OosEvidenceBuilder.Trades(cells);
        return Result.Success(new FullTestOutcome(runs, view, instruments, instrumentsRun, defaultTrades, oos, [.. oos.Select(t => sources[t])]));
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

    // the dependence block comes from the verdict: its EnoughByNEff is judged against the adjusted minimum of gate 1
    private static EvidenceView EvidenceOf(OosEvidence e, DependenceAssessment? dependence) =>
        new(e.OosTrades, Finite(e.ExpectancyR), Finite(e.ProfitFactor), e.Folds, e.PositiveFolds, Finite(e.MaxDrawdownR), e.CalibrationErrorPp, e.Method, DependenceView.Of(dependence));

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    /// <summary>The instruments done in the timeframes before this one, added to the count of this one.</summary>
    private sealed class Offset(IProgress<int> inner, int offset) : IProgress<int>
    {
        public void Report(int value) => inner.Report(offset + value);
    }
}
