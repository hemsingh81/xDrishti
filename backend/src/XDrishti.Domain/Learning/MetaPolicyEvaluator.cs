using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The meta-labelling policy (P6-13, ADR 0075): turns a calibrated probability into take or skip for a candidate entry and says honestly whether that beats taking everything. On the purged folds of P6-05 the model recipe is re-fitted on the train rows
/// and scores the test rows; the threshold used in a fold is chosen only from the out-of-fold scores of earlier folds (purged and embargoed like a train set), maximising net R per candidate under a minimum kept share. Never from the fold itself, never from the hold-out.
/// Judged on skill, not on lift alone: kept must beat skipped, and the lift must beat a random skip of the same share (skipping any share raises R per candidate when taking everything loses), with margins and a standard-error bound widened for the trials.
/// Pure: no clock, no random, no I/O; sequential sums. It takes no CancellationToken: it is a bounded in-memory computation of one model fit per fold, and the caller (the handler) is where the token is checked.
/// </summary>
public static class MetaPolicyEvaluator
{
    private sealed record Scored(ValidationRow Row, double Score, string Group);

    private sealed record FoldInfo(ValidationFold Fold, DateOnly Start, long FirstDecision, List<Scored> Scores);

    /// <summary>Evaluates the policy of one model recipe.</summary>
    /// <param name="rows">Development rows (not the hold-out), unique keys, labels 0 or 1, with the features the model needs.</param>
    /// <param name="model">The champion's recipe: fits on train rows only.</param>
    /// <param name="options">The policy knobs.</param>
    /// <param name="validation">The fold knobs of P6-05.</param>
    public static Result<MetaPolicyReport> Evaluate(IReadOnlyList<ValidationRow> rows, IScorerFactory model, MetaPolicyOptions options, ValidationOptions validation)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validation);
        var problems = options.Problems().Concat(validation.Problems()).ToList();
        if (problems.Count > 0)
        {
            return Fail("policy.options", string.Join("; ", problems));
        }

        var check = WalkForwardValidator.CheckRows(rows, validation);
        if (check is not null)
        {
            return Result.Failure<MetaPolicyReport>(check);
        }

        if (options.Split != MetaSplit.None && rows.FirstOrDefault(r => CellId.TryParse(r.CellKey) is null) is { } unparsed)
        {
            return Fail("policy.cell_key", "Row " + unparsed.Key + " has a cell key that is not strategy|tf|side|style|exit|regime; a split by group cannot read it.");
        }

        var ordered = rows.OrderBy(r => r.TradeDate).ThenBy(r => r.DecisionEpochSeconds).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var from = ordered[0].TradeDate;
        var to = ordered[^1].TradeDate;
        var (cuts, _) = WalkForwardValidator.Cuts(ordered, validation);
        var infos = new List<FoldInfo>();
        foreach (var cut in cuts.Where(c => c.Fold.Usable))
        {
            var scorer = model.Fit(cut.Train);
            var scores = new List<Scored>(cut.Test.Length);
            foreach (var row in cut.Test)
            {
                var p = scorer.Score(row);
                if (!double.IsFinite(p) || p is < 0 or > 1)
                {
                    return Fail("validation.bad_probability", string.Create(CultureInfo.InvariantCulture, $"{model.Name} gave {p} for {row.Key}; a probability is finite and in [0, 1]."));
                }

                scores.Add(new Scored(row, p, GroupOf(options.Split, row.CellKey)));
            }

            infos.Add(new FoldInfo(cut.Fold, cut.Fold.TestStart, cut.Test.Min(r => r.DecisionEpochSeconds), scores));
        }

        if (infos.Count == 0)
        {
            return Fail("validation.no_folds", string.Create(CultureInfo.InvariantCulture, $"No usable fold in {from:yyyy-MM-dd} to {to:yyyy-MM-dd}."));
        }

        var folds = new List<MetaPolicyFold>();
        var evaluated = new List<(Scored Scored, bool Kept)>();
        var pooled = MetaPolicyTotals.Empty;
        for (var i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            var embargoFrom = info.Start.AddDays(-validation.EmbargoDays);
            var prior = infos.Take(i)
                .SelectMany(f => f.Scores)
                .Where(s => s.Row.TradeDate < embargoFrom && s.Row.ResolvedEpochSeconds + HoldOut.BarSeconds < info.FirstDecision)
                .ToList();
            if (prior.Count < options.MinThresholdRows)
            {
                folds.Add(new MetaPolicyFold(info.Fold, false, [], MetaPolicyTotals.Empty, string.Create(CultureInfo.InvariantCulture, $"{prior.Count} earlier scores usable, {options.MinThresholdRows} needed to choose a threshold")));
                continue;
            }

            var thresholds = ThresholdsFor(prior, info.Scores.Select(s => s.Group).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), options);
            if (thresholds.All(t => t.Threshold is null))
            {
                folds.Add(new MetaPolicyFold(info.Fold, false, thresholds, MetaPolicyTotals.Empty, "every group had too few earlier scores and takes everything: nothing was tested"));
                continue;
            }

            var byGroup = thresholds.ToDictionary(t => t.Group, t => t.Threshold, StringComparer.Ordinal);
            var totals = MetaPolicyTotals.Empty;
            foreach (var s in info.Scores)
            {
                var kept = byGroup[s.Group] is not { } t || s.Score >= t;
                evaluated.Add((s, kept));
                totals = totals.Plus(MetaPolicyTotals.One(kept, s.Row.R));
            }

            pooled = pooled.Plus(totals);
            folds.Add(new MetaPolicyFold(info.Fold, true, thresholds, totals));
        }

        var recent = MetaPolicyTotals.Empty;
        foreach (var (s, kept) in evaluated.OrderBy(e => e.Scored.Row.DecisionEpochSeconds).ThenBy(e => e.Scored.Row.Key, StringComparer.Ordinal).TakeLast(options.LastTrades))
        {
            recent = recent.Plus(MetaPolicyTotals.One(kept, s.Row.R));
        }

        var everything = infos.SelectMany(f => f.Scores).ToList();
        var policy = ThresholdsFor(everything, everything.Select(s => s.Group).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), options);
        var (outcome, reasons) = Judge(folds, pooled, recent, infos.Count, options, validation);
        return Result.Success(new MetaPolicyReport(outcome, reasons, options, ordered.Length, from, to, infos.Count, folds, pooled, recent, policy, Limits(validation, options, ordered.Length, from, to, infos.Count)));
    }

    private static (MetaPolicyOutcome Outcome, List<string> Reasons) Judge(List<MetaPolicyFold> folds, MetaPolicyTotals pooled, MetaPolicyTotals recent, int usable, MetaPolicyOptions o, ValidationOptions v)
    {
        var c = CultureInfo.InvariantCulture;
        var reasons = new List<string>();
        var evaluable = folds.Where(f => f.Evaluable).ToList();
        if (evaluable.Count < o.MinEvaluableFolds)
        {
            reasons.Add(string.Create(c, $"{evaluable.Count} evaluable folds, {o.MinEvaluableFolds} needed (the first fold has no earlier scores to choose a threshold)."));
            return (MetaPolicyOutcome.NotEnoughEvidence, reasons);
        }

        var factor = MultipleTesting.Factor(o.Trials);
        var needed = (int)Math.Ceiling((evaluable.Count * v.FoldsBetterShare) - 1e-9);
        var helped = evaluable.Count(f => Beats(f.Totals, o, factor));
        var fails = false;
        if (helped < needed)
        {
            reasons.Add(string.Create(c, $"The policy showed skill in {helped} of {evaluable.Count} evaluable folds; {needed} needed (a fold counts when both sides have {o.MinSideTrades} trades, the lift is above {o.MinLiftR * factor:F3} R per candidate and above a random skip of the same share, and kept beat skipped by more than {o.MinSkillR * factor:F3} R)."));
            fails = true;
        }

        if (!Beats(pooled, o, factor))
        {
            reasons.Add(string.Create(c, $"Pooled out of sample the policy gives {pooled.PolicyPerCandidate:F3} R per candidate against {pooled.BaselinePerCandidate:F3} R for taking everything (lift {pooled.Lift:F3}; a random skip of the same share would give {pooled.RandomLift:F3}, so {pooled.ExcessLift:F3} is skill; {o.MinLiftR * factor:F3} needed); kept minus skipped mean R is {Fmt(pooled.Skill)}, {o.MinSkillR * factor:F3} needed."));
            fails = true;
        }
        else if (!Significant(pooled, o, factor))
        {
            reasons.Add(string.Create(c, $"Kept minus skipped mean R is {Fmt(pooled.Skill)} with a standard error of {Fmt(pooled.SkillStandardError)}; it must exceed {o.SignificanceZ * factor:F2} standard errors ({o.Trials} trials) to be more than chance."));
            fails = true;
        }

        if (fails)
        {
            reasons.Add("Taking everything is as good or better: the score does not help selection on this data.");
            return (MetaPolicyOutcome.DoesNotHelp, reasons);
        }

        var flagged = false;
        if (pooled.KeptShare < 0.5)
        {
            reasons.Add(string.Create(c, $"Flag (doc 09 section 14): out of sample the policy removes {1 - pooled.KeptShare:P0} of the trades, more than half."));
            flagged = true;
        }

        if (recent.Kept >= o.MinSideTrades && recent.Skipped >= o.MinSideTrades && recent.MeanSkipped >= recent.MeanKept)
        {
            reasons.Add(string.Create(c, $"Flag (doc 09 section 14): over the last {recent.Candidates} evaluated trades the skipped ones were not worse than the kept ones."));
            flagged = true;
        }

        if (usable < v.MinFoldsForVerdict)
        {
            reasons.Add(string.Create(c, $"Only {usable} usable folds, {v.MinFoldsForVerdict} needed for a verdict: at best inconclusive."));
            flagged = true;
        }

        if (flagged)
        {
            return (MetaPolicyOutcome.Inconclusive, reasons);
        }

        reasons.Add(string.Create(c, $"The policy showed skill in {helped} of {evaluable.Count} evaluable folds and pooled (lift {pooled.Lift:F3} R per candidate, {pooled.ExcessLift:F3} beyond a random skip; kept minus skipped {Fmt(pooled.Skill)} R, significant at {o.SignificanceZ * factor:F2} standard errors, {o.Trials} trials)."));
        return (MetaPolicyOutcome.Helps, reasons);
    }

    private static bool Beats(MetaPolicyTotals t, MetaPolicyOptions o, double factor) =>
        t.Kept >= o.MinSideTrades && t.Skipped >= o.MinSideTrades
        && t.Lift > o.MinLiftR * factor && t.ExcessLift > o.MinLiftR * factor
        && t.Skill is { } skill && skill > o.MinSkillR * factor;

    private static bool Significant(MetaPolicyTotals t, MetaPolicyOptions o, double factor) =>
        t.Skill is { } skill && t.SkillStandardError is { } se && skill > o.SignificanceZ * factor * se;

    private static string Fmt(double? v) => v is { } x ? x.ToString("F3", CultureInfo.InvariantCulture) : "n/a";

    private static List<MetaPolicyThreshold> ThresholdsFor(List<Scored> basis, IEnumerable<string> groups, MetaPolicyOptions options)
    {
        var result = new List<MetaPolicyThreshold>();
        foreach (var group in groups)
        {
            var mine = basis.Where(s => s.Group == group).ToList();
            result.Add(mine.Count < options.MinThresholdRows ? new MetaPolicyThreshold(group, null, mine.Count, 1) : Choose(group, mine, options));
        }

        return result;
    }

    private static MetaPolicyThreshold Choose(string group, List<Scored> rows, MetaPolicyOptions options)
    {
        var best = 0.0;
        var bestValue = double.NegativeInfinity;
        var bestShare = 1.0;
        for (var k = 0; k * options.ThresholdStep < 1; k++)
        {
            var t = Math.Round(k * options.ThresholdStep, 10);
            var kept = 0;
            var sum = 0.0;
            foreach (var s in rows)
            {
                if (s.Score >= t)
                {
                    kept++;
                    sum += s.Row.R;
                }
            }

            var share = kept / (double)rows.Count;
            if (share < options.MinKeptShare)
            {
                continue;
            }

            if (sum > bestValue)
            {
                best = t;
                bestValue = sum;
                bestShare = share;
            }
        }

        return new MetaPolicyThreshold(group, best, rows.Count, bestShare);
    }

    private static string GroupOf(MetaSplit split, string cellKey)
    {
        var id = split == MetaSplit.None ? null : CellId.TryParse(cellKey);
        return id is null
            ? "*"
            : split switch
            {
                MetaSplit.Style => id.Style,
                MetaSplit.Side => id.Side,
                _ => id.Style + "/" + id.Side,
            };
    }

    private static List<string> Limits(ValidationOptions v, MetaPolicyOptions options, int rows, DateOnly from, DateOnly to, int folds)
    {
        var c = CultureInfo.InvariantCulture;
        return
        [
            string.Create(c, $"{rows} trades, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, {folds} usable folds; the first fold has no earlier scores, so at most {folds - 1} are evaluable."),
            "Replayed history with modelled fills; the data have been seen before (ISS-19). Net R is on modelled fills.",
            v.HoldOutStart is { } h ? string.Create(c, $"The hold-out from {h:yyyy-MM-dd} was not read.") : "No hold-out was reserved for this evaluation.",
            "The fold figures re-fit the champion's recipe on each fold's train rows; they are not the champion file, which has seen later folds.",
            string.Create(c, $"{options.Trials} trials: the lift, skill and significance margins are widened by {MultipleTesting.Factor(options.Trials):F2}; trying other splits, knobs or models counts as more trials."),
            "Trades within a fold are correlated, so the standard error is optimistic; the per-fold requirement is the check on that.",
            "The proposed thresholds are chosen on all development scores and are optimistic; only the fold figures are out of sample.",
            "Advisory: nothing uses this policy until P7; no sizing multiplier is fitted.",
        ];
    }

    private static Result<MetaPolicyReport> Fail(string code, string message) => Result.Failure<MetaPolicyReport>(Error.Validation(code, message));
}
