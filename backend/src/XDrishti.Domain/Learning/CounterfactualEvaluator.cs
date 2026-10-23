using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The frozen-model counterfactual (P6-12, ADR 0077): does continued learning help? One model recipe, two sides scoring the same rows (same days, same instruments): (a) fitted once on the rows before a cut date and never refitted, (b) refitted at the start of each
/// later window on everything resolved before it (expanding). Both are out of sample; the P6-05 metrics are compared per window and pooled over the windows in which the retrained model has seen something new, with the realised R of taking what each side scores at or
/// above the base rate of its own train set (no searched threshold, so nothing to leak). The frozen side never sees a label resolved after the period's first decision. The period is what the data allow, at most a year; fewer judged windows than the minimum is
/// inconclusive and "no difference" is an ordinary answer. Pure: no clock, no random, no I/O; sequential sums. It takes no CancellationToken: it is a bounded in-memory computation of a few model fits, and the handler is where the token is checked.
/// </summary>
public static class CounterfactualEvaluator
{
    /// <summary>Evaluates the counterfactual of one model recipe.</summary>
    /// <param name="rows">Development rows (not the hold-out), unique keys, labels 0 or 1, with the features the model needs.</param>
    /// <param name="model">The recipe: fits on train rows only.</param>
    /// <param name="options">The counterfactual knobs.</param>
    /// <param name="validation">The fold knobs of P6-05 (minimum train months, embargo, minimum train trades, hold-out).</param>
    public static Result<CounterfactualReport> Evaluate(IReadOnlyList<ValidationRow> rows, IScorerFactory model, CounterfactualOptions options, ValidationOptions validation)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validation);
        var problems = options.Problems().Concat(validation.Problems()).ToList();
        if (problems.Count > 0)
        {
            return Result.Failure<CounterfactualReport>(Error.Validation("counterfactual.options", string.Join("; ", problems)));
        }

        var check = WalkForwardValidator.CheckRows(rows, validation);
        if (check is not null)
        {
            return Result.Failure<CounterfactualReport>(check);
        }

        var c = CultureInfo.InvariantCulture;
        var ordered = rows.OrderBy(r => r.TradeDate).ThenBy(r => r.DecisionEpochSeconds).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var first = ordered[0].TradeDate;
        var last = ordered[^1].TradeDate;

        // The cut: the requested age before the end, but never earlier than the minimum train months after the first trade.
        var earliest = first.AddMonths(validation.MinTrainMonths);
        if (earliest.Day != 1)
        {
            earliest = new DateOnly(earliest.Year, earliest.Month, 1).AddMonths(1);
        }

        var wanted = new DateOnly(last.Year, last.Month, 1).AddMonths(-options.FrozenMonths);
        var cut = wanted < earliest ? earliest : wanted;
        var windows = Windows(cut, last, options.RetrainMonths);
        if (windows.Count == 0)
        {
            return Inconclusive(options, validation, last, null, 0, 0, [], string.Create(c, $"The history ({first:yyyy-MM-dd} to {last:yyyy-MM-dd}) is too short: {validation.MinTrainMonths} months of training must come first, and nothing is left to test after {earliest:yyyy-MM-dd}."));
        }

        var periodEnd = windows[^1].End;
        var test = ordered.Where(r => r.TradeDate >= cut && r.TradeDate <= periodEnd).ToArray();
        if (test.Length == 0)
        {
            return Inconclusive(options, validation, last, cut, 0, 0, [], "No trades fall in the period after the cut.");
        }

        var embargoFrom = cut.AddDays(-validation.EmbargoDays);
        var firstDecision = test.Min(r => r.DecisionEpochSeconds);
        var frozenTrain = ordered.Where(r => r.TradeDate < embargoFrom && r.ResolvedEpochSeconds + HoldOut.BarSeconds < firstDecision).ToArray();
        var ageMonths = MonthsBetween(cut, last);
        if (frozenTrain.Length < validation.MinTrainTrades)
        {
            return Inconclusive(options, validation, last, cut, ageMonths, frozenTrain.Length, [], string.Create(c, $"Only {frozenTrain.Length} trades were known before the cut {cut:yyyy-MM-dd}, {validation.MinTrainTrades} needed to train the frozen model."));
        }

        var frozen = new FittedSide(model.Fit(frozenTrain), Rate(frozenTrain));
        var results = new List<CounterfactualWindow>();
        var judged = new Accumulator();
        var all = new Accumulator();
        var skipped = new List<string>();
        for (var i = 0; i < windows.Count; i++)
        {
            var (start, end) = windows[i];
            var rowsOfWindow = test.Where(r => r.TradeDate >= start && r.TradeDate <= end).ToArray();
            if (rowsOfWindow.Length < options.MinWindowRows)
            {
                skipped.Add(string.Create(c, $"window {i + 1} {start:yyyy-MM-dd}..{end:yyyy-MM-dd} has {rowsOfWindow.Length} trades, {options.MinWindowRows} needed: not scored by either side."));
                continue;
            }

            var from = start.AddDays(-validation.EmbargoDays);
            var decision = rowsOfWindow.Min(r => r.DecisionEpochSeconds);
            var train = ordered.Where(r => r.TradeDate < from && r.ResolvedEpochSeconds + HoldOut.BarSeconds < decision).ToArray();
            var informative = train.Length > frozenTrain.Length;
            var retrained = informative ? new FittedSide(model.Fit(train), Rate(train)) : frozen;
            var window = new Accumulator();
            foreach (var row in rowsOfWindow)
            {
                var pf = frozen.Scorer.Score(row);
                var pr = ReferenceEquals(retrained, frozen) ? pf : retrained.Scorer.Score(row);
                if (!IsProbability(pf) || !IsProbability(pr))
                {
                    var bad = IsProbability(pf) ? pr : pf;
                    return Result.Failure<CounterfactualReport>(Error.Validation("validation.bad_probability", string.Create(c, $"{model.Name} gave {bad} for {row.Key}; a probability is finite and in [0, 1].")));
                }

                window.Add(row, pf, pr, frozen.Rate, retrained.Rate);
            }

            all.Merge(window);
            if (informative)
            {
                judged.Merge(window);
            }

            results.Add(new CounterfactualWindow(i + 1, start, end, rowsOfWindow.Length, rowsOfWindow.Select(r => r.TradeDate).Distinct().Count(), rowsOfWindow.Select(r => InstrumentOf(r.Key)).Distinct(StringComparer.Ordinal).Count(), frozenTrain.Length, train.Length, informative, window.Pool(validation.CalibrationBins)));
        }

        var judgedPool = judged.Rows == 0 ? null : judged.Pool(validation.CalibrationBins);
        var allPool = all.Rows == 0 ? null : all.Pool(validation.CalibrationBins);
        var (outcome, reasons) = Judge(results, judgedPool, options, validation);
        return Result.Success(new CounterfactualReport(outcome, reasons, options, cut, last, ageMonths, frozenTrain.Length, results, judgedPool, allPool, Limits(options, validation, first, last, cut, ageMonths, results, skipped, ordered.Count(r => r.TradeDate > periodEnd))));
    }

    private static (CounterfactualOutcome Outcome, List<string> Reasons) Judge(List<CounterfactualWindow> windows, CounterfactualPooled? pooled, CounterfactualOptions o, ValidationOptions v)
    {
        var c = CultureInfo.InvariantCulture;
        var reasons = new List<string>();
        var judged = windows.Where(w => w.Informative).ToList();
        if (judged.Count < o.MinJudgedWindows || pooled is null)
        {
            reasons.Add(string.Create(c, $"{judged.Count} judged windows (those in which the retrained model has seen labels the frozen one has not), {o.MinJudgedWindows} needed: no verdict, whatever the numbers below show."));
            return (CounterfactualOutcome.Inconclusive, reasons);
        }

        var factor = MultipleTesting.Factor(o.Trials);
        var margin = o.MinGainLogLoss * factor;
        var needed = (int)Math.Ceiling((judged.Count * v.FoldsBetterShare) - 1e-9);
        var retrainWins = judged.Count(w => w.Pooled.MeanGain > margin);
        var frozenWins = judged.Count(w => w.Pooled.MeanGain < -margin);
        var significant = pooled.GainStandardError is { } se && Math.Abs(pooled.MeanGain) > o.SignificanceZ * factor * se;
        var policyGap = pooled.Retrained.Policy.PolicyPerCandidate - pooled.Frozen.Policy.PolicyPerCandidate;
        if (retrainWins >= needed && pooled.MeanGain > margin && significant)
        {
            if (policyGap < 0)
            {
                reasons.Add(string.Create(c, $"Retraining scored better in {retrainWins} of {judged.Count} judged windows, but taking what the retrained model scores high earned {-policyGap:F3} R per candidate less than the frozen one: the two measures disagree."));
                return (CounterfactualOutcome.Inconclusive, reasons);
            }

            reasons.Add(string.Create(c, $"Retraining scored better in {retrainWins} of {judged.Count} judged windows and pooled: log loss gain {pooled.MeanGain:F4} per trade (above {margin:F4} and {o.SignificanceZ * factor:F2} standard errors, {o.Trials} trials); the realised R of its takes was {policyGap:F3} R per candidate higher."));
            return (CounterfactualOutcome.RetrainingHelps, reasons);
        }

        if (frozenWins >= needed && pooled.MeanGain < -margin && significant)
        {
            if (policyGap > 0)
            {
                reasons.Add(string.Create(c, $"The frozen model scored better in {frozenWins} of {judged.Count} judged windows, but taking what the retrained model scores high earned {policyGap:F3} R per candidate more: the two measures disagree."));
                return (CounterfactualOutcome.Inconclusive, reasons);
            }

            reasons.Add(string.Create(c, $"The frozen model scored better in {frozenWins} of {judged.Count} judged windows and pooled: log loss gain of retraining {pooled.MeanGain:F4} per trade (below minus {margin:F4}, beyond {o.SignificanceZ * factor:F2} standard errors, {o.Trials} trials)."));
            return (CounterfactualOutcome.FrozenBetter, reasons);
        }

        reasons.Add(string.Create(c, $"The two sides are not told apart: retraining was better in {retrainWins} and worse in {frozenWins} of {judged.Count} judged windows; pooled log loss gain {pooled.MeanGain:F4} per trade (standard error {(pooled.GainStandardError is { } e ? e.ToString("F4", c) : "n/a")}; {margin:F4} and {o.SignificanceZ * factor:F2} standard errors needed, {o.Trials} trials). Continued learning did not measurably help or hurt on this data."));
        return (CounterfactualOutcome.NoDifference, reasons);
    }

    private static List<(DateOnly Start, DateOnly End)> Windows(DateOnly cut, DateOnly last, int months)
    {
        var windows = new List<(DateOnly, DateOnly)>();
        var start = cut;
        while (start <= last)
        {
            var next = start.AddMonths(months);
            if (next > last.AddDays(1) && last.AddDays(1) < start.AddMonths(1))
            {
                break;
            }

            windows.Add((start, next.AddDays(-1)));
            start = next;
        }

        return windows;
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) => Math.Max(0, ((to.Year - from.Year) * 12) + to.Month - from.Month + (to.Day >= from.Day ? 0 : -1));

    private static string InstrumentOf(string key)
    {
        var parts = key.Split('|');
        return parts.Length > 1 ? parts[1] : key;
    }

    private static double Rate(ValidationRow[] train) => train.Length == 0 ? 0 : train.Count(r => r.Label == 1) / (double)train.Length;

    private static bool IsProbability(double p) => double.IsFinite(p) && p is >= 0 and <= 1;

    private static Result<CounterfactualReport> Inconclusive(CounterfactualOptions options, ValidationOptions validation, DateOnly last, DateOnly? cut, int age, int trainRows, List<CounterfactualWindow> windows, string reason) =>
        Result.Success(new CounterfactualReport(CounterfactualOutcome.Inconclusive, [reason], options, cut, last, age, trainRows, windows, null, null, Limits(options, validation, last, last, cut, age, windows, [], 0)));

    private static List<string> Limits(CounterfactualOptions o, ValidationOptions v, DateOnly first, DateOnly last, DateOnly? cut, int age, List<CounterfactualWindow> windows, List<string> skipped, int unused)
    {
        var c = CultureInfo.InvariantCulture;
        var limits = new List<string>
        {
            cut is { } at
                ? string.Create(c, $"The period of stopped learning is {age} months ({at:yyyy-MM-dd} to {last:yyyy-MM-dd}), what this data allow (about two years in all, a hold-out and {v.MinTrainMonths} months of training first), and at most {o.FrozenMonths} were asked for. It says nothing about longer.")
                : string.Create(c, $"No period of stopped learning could be made from {first:yyyy-MM-dd} to {last:yyyy-MM-dd}."),
            string.Create(c, $"{windows.Count(w => w.Informative)} judged windows of {o.RetrainMonths} months; the first window has the same model on both sides and is not judged. Few windows, and trades within a window are correlated, so the standard error is optimistic."),
            "Both sides score exactly the same trades (same days, same instruments); the frozen side never sees a label resolved after the period's first decision; the retrained side sees only labels resolved before each window.",
            v.HoldOutStart is { } h ? string.Create(c, $"The hold-out from {h:yyyy-MM-dd} was not read.") : "No hold-out was reserved for this evaluation.",
            "Replayed history with modelled fills; the data have been seen before (ISS-19). Take-R takes a trade when the score is at or above the base rate of the side's own train set; that is not a tuned threshold.",
            string.Create(c, $"{o.Trials} trials: the gain margin and significance are widened by {MultipleTesting.Factor(o.Trials):F2}; other periods, schedules or models count as more trials."),
            "Comparison with plan prices is not built (no live record exists yet); no screen (the Journey view is P8).",
        };
        limits.AddRange(skipped);
        if (unused > 0)
        {
            limits.Add(string.Create(c, $"{unused} trades after the last full window were not used by either side."));
        }

        return limits;
    }

    private sealed record FittedSide(IRowScorer Scorer, double Rate);

    private sealed class Accumulator
    {
        private readonly List<double> _frozenScores = [];
        private readonly List<double> _retrainedScores = [];
        private readonly List<int> _labels = [];
        private MetaPolicyTotals _frozenPolicy = MetaPolicyTotals.Empty;
        private MetaPolicyTotals _retrainedPolicy = MetaPolicyTotals.Empty;
        private double _gainSum;
        private double _gainSq;

        public int Rows => _labels.Count;

        public void Add(ValidationRow row, double frozenScore, double retrainedScore, double frozenRate, double retrainedRate)
        {
            _frozenScores.Add(frozenScore);
            _retrainedScores.Add(retrainedScore);
            _labels.Add(row.Label);
            _frozenPolicy = _frozenPolicy.Plus(MetaPolicyTotals.One(frozenScore >= frozenRate, row.R));
            _retrainedPolicy = _retrainedPolicy.Plus(MetaPolicyTotals.One(retrainedScore >= retrainedRate, row.R));
            var gain = Loss(frozenScore, row.Label) - Loss(retrainedScore, row.Label);
            _gainSum += gain;
            _gainSq += gain * gain;
        }

        public void Merge(Accumulator other)
        {
            _frozenScores.AddRange(other._frozenScores);
            _retrainedScores.AddRange(other._retrainedScores);
            _labels.AddRange(other._labels);
            _frozenPolicy = _frozenPolicy.Plus(other._frozenPolicy);
            _retrainedPolicy = _retrainedPolicy.Plus(other._retrainedPolicy);
            _gainSum += other._gainSum;
            _gainSq += other._gainSq;
        }

        public CounterfactualPooled Pool(int bins)
        {
            var n = _labels.Count;
            double? se = null;
            if (n >= 2)
            {
                var variance = Math.Max(0, (_gainSq - (_gainSum * _gainSum / n)) / (n - 1));
                se = Math.Sqrt(variance / n);
            }

            return new CounterfactualPooled(
                n,
                new CounterfactualSide(MetricsCalculator.Compute(_frozenScores, _labels, bins), _frozenPolicy),
                new CounterfactualSide(MetricsCalculator.Compute(_retrainedScores, _labels, bins), _retrainedPolicy),
                n == 0 ? 0 : _gainSum / n,
                se);
        }

        private static double Loss(double score, int label)
        {
            var p = Math.Clamp(score, 1e-15, 1 - 1e-15);
            return -(label == 1 ? Math.Log(p) : Math.Log(1 - p));
        }
    }
}
