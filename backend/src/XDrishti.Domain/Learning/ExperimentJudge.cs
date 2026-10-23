using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Judges one knob change (P6-11, ADR 0072; doc 09 §12). The two runs were made on the same data and the same entries (common random numbers: the replay has no randomness, so "the same" means the same keys); the candidate may skip entries, never add one. The
/// fold windows are those of the exit choice (<see cref="ExitSelector.Windows"/>, ADR 0054). The candidate is better only when: a cheaper assumption is not what changed; at least <c>ceil(share * used folds)</c> folds and the pooled sample improve mean R per trade
/// by more than <see cref="ExperimentOptions.RequiredImprovementR"/> (widened by the trials); the total R after the expected live gap does not fall by more than the allowed share; and at least <see cref="ValidationOptions.DefaultMinFolds"/> folds were
/// used. Fewer folds are at best inconclusive. Pure: no clock, no random, no I/O.
/// </summary>
public static class ExperimentJudge
{
    /// <summary>Judges the candidate against the baseline.</summary>
    /// <param name="knob">The one knob that differs between the runs.</param>
    /// <param name="trades">The entries of the baseline run with the candidate's result for each; unique keys.</param>
    /// <param name="options">The rules.</param>
    /// <param name="fillCost">For a slippage knob, how the stored fill rules of the two runs compare; it is required there and ignored for any other knob.</param>
    public static Result<ExperimentReport> Judge(ExperimentKnob knob, IReadOnlyList<ExperimentTrade> trades, ExperimentOptions options, FillCostChange fillCost = FillCostChange.NotApplicable)
    {
        ArgumentNullException.ThrowIfNull(knob);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);
        var problems = knob.Problems().Concat(options.Problems()).ToList();
        if (problems.Count > 0)
        {
            return Fail("experiment.options", string.Join("; ", problems));
        }

        if (knob.Kind == ExperimentKnobKind.Slippage && fillCost == FillCostChange.NotApplicable)
        {
            return Fail("experiment.options", "a slippage knob needs the comparison of the stored fill rules of the two runs");
        }

        var cheaper = knob.Kind == ExperimentKnobKind.Slippage && fillCost is (FillCostChange.Cheaper or FillCostChange.Mixed);
        var check = Check(trades, options);
        if (check is not null)
        {
            return Result.Failure<ExperimentReport>(check);
        }

        var c = CultureInfo.InvariantCulture;
        var minFolds = Math.Max(options.MinFolds, ValidationOptions.DefaultMinFolds);
        var rules = options with
        {
            MinFolds = minFolds,
            MinFoldTrades = Math.Max(options.MinFoldTrades, ExperimentOptions.MinFoldTradesFloor),
            ExpectedLiveGapR = Math.Max(options.ExpectedLiveGapR, ExperimentOptions.LiveGapFloorR),
        };
        var required = rules.RequiredImprovementR;
        var ordered = trades.OrderBy(t => t.TradeDate).ThenBy(t => t.Key, StringComparer.Ordinal).ToArray();
        var from = ordered[0].TradeDate;
        var to = ordered[^1].TradeDate;
        var (windows, dropped) = ExitSelector.Windows(from, to, new SelectionOptions(options.MinTrainMonths, options.TestMonths));

        var folds = new List<ExperimentFold>();
        var baseSum = 0.0;
        var baseCount = 0;
        var candSum = 0.0;
        var candCount = 0;
        for (var i = 0; i < windows.Count; i++)
        {
            var (start, end) = windows[i];
            var inside = ordered.Where(t => t.TradeDate >= start && t.TradeDate <= end).ToArray();
            var baseline = inside.Select(t => t.BaselineR).ToArray();
            var candidate = inside.Where(t => t.CandidateR is not null).Select(t => t.CandidateR!.Value).ToArray();
            if (baseline.Length < rules.MinFoldTrades || candidate.Length < rules.MinFoldTrades)
            {
                var reason = string.Create(c, $"{baseline.Length} baseline and {candidate.Length} candidate trades, {rules.MinFoldTrades} needed on each side");
                folds.Add(new ExperimentFold(i + 1, start, end, baseline.Length, candidate.Length, Mean(baseline), Mean(candidate), reason, false));
                continue;
            }

            var bm = baseline.Sum() / baseline.Length;
            var cm = candidate.Sum() / candidate.Length;
            folds.Add(new ExperimentFold(i + 1, start, end, baseline.Length, candidate.Length, bm, cm, null, cm - bm > required));
            baseSum += baseline.Sum();
            baseCount += baseline.Length;
            candSum += candidate.Sum();
            candCount += candidate.Length;
        }

        var used = folds.Count(f => f.Usable);
        var better = folds.Count(f => f.Better);
        var needed = (int)Math.Ceiling((options.FoldsBetterShare * used) - 1e-9);
        double? pooledBase = baseCount > 0 ? baseSum / baseCount : null;
        double? pooledCand = candCount > 0 ? candSum / candCount : null;

        var candidateTrades = ordered.Count(t => t.CandidateR is not null);
        var baseTotal = ordered.Sum(t => t.BaselineR - rules.ExpectedLiveGapR);
        var candTotal = ordered.Where(t => t.CandidateR is not null).Sum(t => t.CandidateR!.Value - rules.ExpectedLiveGapR);

        var reasons = new List<string>();
        if (cheaper)
        {
            reasons.Add(fillCost == FillCostChange.Mixed
                ? "the stored fill rules of the two runs differ in slippage components in both directions, so they cannot be ordered: a cheaper assumption cannot win, whatever the numbers"
                : "the stored fill rules show the candidate assumes less slippage than the baseline (read from the runs, not from the typed values): a cheaper assumption cannot win, whatever the numbers");
        }

        if (used == 0)
        {
            reasons.Add(string.Create(c, $"no fold is usable: {windows.Count} windows after {options.MinTrainMonths} months, {dropped} short final window dropped, {rules.MinFoldTrades} trades needed on each side"));
        }
        else
        {
            if (better < needed)
            {
                reasons.Add(string.Create(c, $"better in {better} of {used} used folds, {needed} needed"));
            }

            if (pooledBase is { } pb && pooledCand is { } pc && !(pc - pb > required))
            {
                reasons.Add(string.Create(c, $"the pooled improvement {pc - pb:F4} R per trade does not exceed the required {required:F4}"));
            }
        }

        var floor = baseTotal - (options.MaxTotalRDrop * Math.Abs(baseTotal));
        if (candTotal < floor - 1e-9)
        {
            reasons.Add(string.Create(c, $"total R after the live gap falls from {baseTotal:F2} to {candTotal:F2}, more than the {options.MaxTotalRDrop:P0} allowed"));
        }

        ExperimentOutcome outcome;
        if (cheaper)
        {
            outcome = ExperimentOutcome.CheaperAssumption;
        }
        else if (used == 0)
        {
            outcome = ExperimentOutcome.Inconclusive;
        }
        else if (reasons.Count > 0)
        {
            outcome = ExperimentOutcome.NotBetter;
        }
        else if (used < minFolds)
        {
            outcome = ExperimentOutcome.Inconclusive;
            reasons.Add(string.Create(c, $"only {used} used folds, {minFolds} needed to call it better"));
        }
        else if (options.NoHoldOut)
        {
            outcome = ExperimentOutcome.Inconclusive;
            reasons.Add("no hold-out was kept for this experiment: with every trade looked at, no change can be called better");
        }
        else
        {
            outcome = ExperimentOutcome.Better;
        }

        return Result.Success(new ExperimentReport(
            knob, rules, outcome, ordered.Length, candidateTrades, from, to, folds, better, needed, required, pooledBase, pooledCand, baseTotal, candTotal, reasons, LimitsOf(rules, options, ordered.Length, from, to, used, minFolds)));
    }

    private static Error? Check(IReadOnlyList<ExperimentTrade> trades, ExperimentOptions options)
    {
        if (trades.Count == 0)
        {
            return Error.Validation("experiment.no_trades", "There are no trades.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in trades)
        {
            if (!keys.Add(t.Key))
            {
                return Error.Conflict("experiment.duplicate_key", "Two trades have the key " + t.Key + ".");
            }

            if (!double.IsFinite(t.BaselineR) || (t.CandidateR is { } r && !double.IsFinite(r)))
            {
                return Error.Validation("experiment.bad_trade", "Trade " + t.Key + " needs a finite R.");
            }

            if (options.HoldOutStart is { } h && t.TradeDate >= h)
            {
                return Error.Validation("experiment.holdout_touched", "Trade " + t.Key + " is in the hold-out (from " + h.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "); development may not read it.");
            }
        }

        return null;
    }

    private static List<string> LimitsOf(ExperimentOptions rules, ExperimentOptions asked, int trades, DateOnly from, DateOnly to, int used, int minFolds)
    {
        var c = CultureInfo.InvariantCulture;
        var limits = new List<string>
        {
            string.Create(c, $"{trades} trades, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, {used} used folds of {rules.TestMonths} months."),
            "Replayed history with modelled fills. The knob was chosen by looking at this history, so the folds show whether the gain is spread over time, not that it holds out of sample for the choice (ISS-19).",
            string.Create(c, $"The expected live gap of {rules.ExpectedLiveGapR:F3} R per trade is an assumption until live fills are measured (P10); it is taken off both sides and only matters for the total."),
        };
        limits.Add(rules.HoldOutStart is { } h
            ? string.Create(c, $"The hold-out from {h:yyyy-MM-dd} was not read.")
            : "No hold-out was reserved for this experiment.");
        if (used < minFolds)
        {
            limits.Add(string.Create(c, $"Fewer than {minFolds} folds: no change can be called better, at best inconclusive."));
        }

        if (asked.MinFoldTrades < rules.MinFoldTrades)
        {
            limits.Add(string.Create(c, $"The minimum of {asked.MinFoldTrades} trades per side in a fold was raised to the floor of {rules.MinFoldTrades}."));
        }

        if (asked.ExpectedLiveGapR < rules.ExpectedLiveGapR)
        {
            limits.Add(string.Create(c, $"The expected live gap of {asked.ExpectedLiveGapR:F3} R was raised to the floor of {rules.ExpectedLiveGapR:F3} R: a lower gap would flatter a candidate that trades more."));
        }

        limits.Add("An experiment that is refused or fails before it is judged is not recorded and does not count as a trial, though it may have been looked at.");
        if (asked.MinFolds < minFolds)
        {
            limits.Add(string.Create(c, $"The minimum of {asked.MinFolds} folds was raised to the floor of {minFolds}."));
        }

        if (rules.Trials > 1)
        {
            limits.Add(string.Create(c, $"{rules.Trials} experiments in {ExperimentOptions.TrialWindowDays} days: the required improvement is widened to {rules.RequiredImprovementR:F4} R per trade."));
        }

        return limits;
    }

    private static double? Mean(double[] values) => values.Length == 0 ? null : values.Sum() / values.Length;

    private static Result<ExperimentReport> Fail(string code, string message) => Result.Failure<ExperimentReport>(Error.Validation(code, message));
}
