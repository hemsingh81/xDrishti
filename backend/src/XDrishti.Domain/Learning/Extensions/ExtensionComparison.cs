using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The comparison of P13-05: does adding an extension to <c>tf-1</c> improve out-of-sample net R per candidate under the ADR 0075 take policy, by more than a margin widened for the number of looks? Model A sees the first <c>baseWidth</c> features of the very rows model B sees in full;
/// the same recipe is fitted twice per fold; the take thresholds of each model come only from its own earlier out-of-fold scores. Pure: no clock, no I/O, no random but the seeded bootstrap, sums in a canonical order, so equal inputs give an equal report and the order of the input rows and sessions does not matter.
/// It refuses when the trial is not already in the ledger, before any model is fitted.
/// </summary>
public static class ExtensionComparison
{
    private sealed record Paired(DateOnly Date, double R, double D, bool Flip);

    /// <summary>Compares model B (<c>tf-1</c> plus the extension) with model A (<c>tf-1</c>).</summary>
    /// <param name="rows">Development rows with B's width: <paramref name="baseWidth"/> catalog values then the extension values.</param>
    /// <param name="baseWidth">The number of <c>tf-1</c> values.</param>
    /// <param name="group">The extension.</param>
    /// <param name="recipe">The one recipe, fitted for A and for B.</param>
    /// <param name="ledgerHashes">The family's distinct trial hashes from the ledger; must include this trial's hash.</param>
    /// <param name="sessions">The trading sessions (any order).</param>
    /// <param name="options">The knobs.</param>
    public static Result<ExtensionReport> Compare(IReadOnlyList<ValidationRow> rows, int baseWidth, ExtensionGroup group, IScorerFactory recipe, IReadOnlyCollection<string> ledgerHashes, IReadOnlyList<DateOnly> sessions, ExtensionComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(ledgerHashes);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Problems().ToList();
        if (group.Problem() is { } groupProblem)
        {
            problems.Add(groupProblem);
        }

        if (baseWidth < 1)
        {
            problems.Add("the base width must be at least 1");
        }

        if (problems.Count > 0)
        {
            return Result.Failure<ExtensionReport>(Error.Validation("extensions.options", string.Join("; ", problems)));
        }

        if (ExtensionScoring.CheckRows(rows, baseWidth + group.Names.Count, options.HoldOutStart) is { } bad)
        {
            return Result.Failure<ExtensionReport>(bad);
        }

        var ordered = rows.OrderBy(r => r.TradeDate).ThenBy(r => r.DecisionEpochSeconds).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var from = ordered[0].TradeDate;
        var to = ordered[^1].TradeDate;
        var trial = ExtensionTrial.Of(group, recipe.Name, options, from, to);
        var distinct = ledgerHashes.Distinct(StringComparer.Ordinal).ToArray();
        if (!distinct.Contains(trial.Hash, StringComparer.Ordinal))
        {
            return Result.Failure<ExtensionReport>(Error.Validation("extensions.trial_not_in_ledger", "The trial " + trial.Hash + " is not in the ledger of " + trial.Family + "; append its ledger row first, then compare."));
        }

        var factor = MultipleTesting.Factor(distinct.Length);
        var sorted = sessions.Distinct().Order().ToArray();
        if (SessionsProblem(ordered, sorted, options) is { } sessionsProblem)
        {
            return Result.Failure<ExtensionReport>(Error.Validation("extensions.sessions", sessionsProblem));
        }

        var cut = CalendarFolds.Cut(ordered, sorted, options);
        var viewA = ExtensionScoring.ViewA(ordered, baseWidth);
        var scoredResult = ExtensionScoring.Score(cut.Folds.Where(f => f.Usable), viewA, recipe);
        if (scoredResult.IsFailure)
        {
            return Result.Failure<ExtensionReport>(scoredResult.Error);
        }

        var scored = scoredResult.Value;
        var results = new List<ExtensionFoldResult>();
        var pairs = new List<Paired>();
        var positive = 0;
        var evaluable = 0;
        var usedIndex = 0;
        foreach (var fold in cut.Folds)
        {
            if (!fold.Usable)
            {
                results.Add(new ExtensionFoldResult(fold.Start, fold.End, fold.Train.Count, fold.Test.Count, false, false, fold.SkipReason, null, null, 0, 0, null));
                continue;
            }

            var s = scored[usedIndex];
            var tA = ExtensionScoring.ThresholdFor(scored, usedIndex, 0, options);
            var tB = ExtensionScoring.ThresholdFor(scored, usedIndex, 1, options);
            usedIndex++;
            if (tA is not { } ta || tB is not { } tb)
            {
                results.Add(new ExtensionFoldResult(fold.Start, fold.End, fold.Train.Count, fold.Test.Count, true, false, "too few earlier out-of-fold scores to choose a threshold for both models", tA, tB, 0, 0, null));
                continue;
            }

            double sum = 0;
            var flips = 0;
            for (var k = 0; k < fold.Test.Count; k++)
            {
                var row = fold.Test[k];
                var takeA = s.A[k] >= ta;
                var takeB = s.B[k] >= tb;
                var d = ((takeB ? 1 : 0) - (takeA ? 1 : 0)) * row.R;
                sum += d;
                flips += takeA != takeB ? 1 : 0;
                pairs.Add(new Paired(row.TradeDate, row.R, d, takeA != takeB));
            }

            var delta = sum / fold.Test.Count;
            evaluable++;
            positive += delta > 0 ? 1 : 0;
            results.Add(new ExtensionFoldResult(fold.Start, fold.End, fold.Train.Count, fold.Test.Count, true, true, null, ta, tb, fold.Test.Count, flips, delta));
        }

        var n = pairs.Count;
        var flipCount = pairs.Count(p => p.Flip);
        double? deltaR = n > 0 ? Sum(pairs.Select(p => p.D)) / n : null;
        var stated = BlockBootstrap.Bounds(Blocks(pairs, sorted, options, options.BlockSessions), options.Draws, options.Seed, options.Level);
        var sensitivityRun = options.Block == BlockKind.Sessions && options.SensitivityBlockSessions > 0;
        var sensitivity = sensitivityRun ? BlockBootstrap.Bounds(Blocks(pairs, sorted, options, options.SensitivityBlockSessions), options.Draws, options.Seed, options.Level) : null;

        ProbabilityMetrics? metricsA = null, metricsB = null;
        if (scored.Count > 0)
        {
            var labels = scored.SelectMany(f => f.Fold.Test.Select(r => r.Label)).ToArray();
            metricsA = MetricsCalculator.Compute(scored.SelectMany(f => f.A).ToArray(), labels);
            metricsB = MetricsCalculator.Compute(scored.SelectMany(f => f.B).ToArray(), labels);
        }

        double? meanR2 = flipCount > 0 ? Sum(pairs.Where(p => p.Flip).Select(p => p.R * p.R)) / flipCount : null;
        double? naive = meanR2 is { } m2 && n > 0 ? Math.Sqrt(flipCount / (double)n * m2 / n) : null;
        double? designEffect = stated.StandardError is { } se && naive is > 0 ? se / naive : null;
        double? dLogLoss = metricsA is not null && metricsB is not null ? metricsB.LogLoss - metricsA.LogLoss : null;
        double? dBrier = metricsA is not null && metricsB is not null ? metricsB.Brier - metricsA.Brier : null;
        var input = new ExtensionVerdictInput(evaluable, n, flipCount, positive, stated.Lower, stated.Upper, sensitivity?.Lower, sensitivity?.Upper, sensitivityRun, dLogLoss, dBrier, metricsB?.EcePp, factor);
        var verdict = ExtensionVerdictRule.Decide(input, options);
        return Result.Success(new ExtensionReport(
            group, trial, options, recipe.Name, from, to, ordered.Length, distinct.Length, factor, verdict.Verdict, verdict.Missing, verdict.Failed, results, cut.IncompleteDropped,
            n, flipCount, positive, deltaR, stated, sensitivity, dLogLoss, dBrier, metricsB?.EcePp, meanR2, naive, designEffect, Limits(options, ordered.Length, from, to, scored.Count, factor, distinct.Length, cut.IncompleteDropped)));
    }

    // Every row date must be a session, and the list must reach back EmbargoSessions sessions before the first window; otherwise the embargo and the block grid would be cut from a calendar that does not know the rows.
    private static string? SessionsProblem(ValidationRow[] ordered, DateOnly[] sessions, ExtensionComparisonOptions options)
    {
        var known = new HashSet<DateOnly>(sessions);
        if (ordered.FirstOrDefault(r => !known.Contains(r.TradeDate)) is { } stray)
        {
            return "Row " + stray.Key + " is dated " + stray.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", which is not in the session list.";
        }

        var firstWindow = CalendarFolds.Boundary(ordered[0].TradeDate.AddMonths(options.MinTrainMonths), options.Unit);
        return CalendarFolds.LowerBound(sessions, firstWindow) < options.EmbargoSessions
            ? string.Create(CultureInfo.InvariantCulture, $"The session list has fewer than {options.EmbargoSessions} sessions before the first window ({firstWindow:yyyy-MM-dd}); the embargo cannot be cut.")
            : null;
    }

    private static double Sum(IEnumerable<double> values)
    {
        var sum = 0.0;
        foreach (var v in values)
        {
            sum += v;
        }

        return sum;
    }

    // Blocks keyed and sorted before drawing: runs of consecutive sessions, or calendar quarters (a season). Sums in the order of the rows, which are in canonical order.
    // The session grid starts at the first paired row's session, so a block of 5 sessions holds 5 consecutive sessions of the analysed rows.
    private static List<BootstrapBlock> Blocks(List<Paired> pairs, DateOnly[] sessions, ExtensionComparisonOptions options, int blockSessions)
    {
        var blocks = new SortedDictionary<long, (double Sum, int Count)>();
        var anchor = pairs.Count == 0 ? 0 : CalendarFolds.LowerBound(sessions, pairs[0].Date.AddDays(1)) - 1;
        foreach (var p in pairs)
        {
            var key = options.Block == BlockKind.Season
                ? (p.Date.Year * 4L) + ((p.Date.Month - 1) / 3)
                : (CalendarFolds.LowerBound(sessions, p.Date.AddDays(1)) - 1 - anchor) / blockSessions;
            blocks[key] = blocks.TryGetValue(key, out var b) ? (b.Sum + p.D, b.Count + 1) : (p.D, 1);
        }

        return [.. blocks.Values.Select(b => new BootstrapBlock(b.Sum, b.Count))];
    }

    private static List<string> Limits(ExtensionComparisonOptions o, int rows, DateOnly from, DateOnly to, int usedFolds, double factor, int trials, int incomplete)
    {
        var c = CultureInfo.InvariantCulture;
        return
        [
            string.Create(c, $"{rows} trades, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, {usedFolds} used {(o.Unit == FoldUnit.Month ? "monthly" : "quarterly")} folds; the first fold has no earlier scores for a threshold, so at most {Math.Max(0, usedFolds - 1)} are evaluable; {incomplete} incomplete windows dropped."),
            "Replayed history with modelled fills; gross R unless the stored charge sheet makes the set net (ISS-25); survivorship (ISS-22). Net R per candidate, not per taken candidate.",
            string.Create(c, $"{trials} trials of this family: the gain, log loss and Brier margins are widened by {factor:F2} (1 + 0.25 log2 N); another subset or recipe counts as another trial."),
            o.Block == BlockKind.Season
                ? "Blocks are calendar quarters (seasons); few of them give wide intervals."
                : string.Create(c, $"Blocks of {o.BlockSessions} session(s), and {o.SensitivityBlockSessions} as a sensitivity: KEEP needs both lower bounds. Candidates on adjacent days share a regime."),
            o.HoldOutStart is { } h ? string.Create(c, $"The hold-out from {h:yyyy-MM-dd} was not read.") : "No hold-out was reserved for this evaluation.",
            "Advisory research only: nothing in a decision path reads this result.",
        ];
    }
}
