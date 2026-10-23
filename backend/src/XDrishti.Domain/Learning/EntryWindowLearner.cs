using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Learns, per cell, the best entry-time window from labelled trades (P6-14, ADR 0069). No model: the win rate of each contiguous run of entry-time buckets is shrunk toward the cell's own rate (the prior of P6-01), the best run with enough trades, a lift and no
/// weak bucket inside is the window, and the window is then re-learnt on the train rows of the purged walk-forward folds of P6-05 and judged on each fold's test rows. The honest outcome may be "no stable window". Pure: no clock, no random, no I/O. It proposes;
/// nothing here is applied to a strategy or the scanner.
/// </summary>
public static class EntryWindowLearner
{
    /// <summary>Learns the windows of every cell in <paramref name="rows"/>.</summary>
    public static Result<EntryWindowReport> Learn(IReadOnlyList<ValidationRow> rows, EntryWindowOptions options, ValidationOptions validation)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validation);
        var problems = options.Problems().Concat(validation.Problems()).ToList();
        if (problems.Count > 0)
        {
            return Fail("entry_windows.options", string.Join("; ", problems));
        }

        if (rows.Count == 0)
        {
            return Fail("entry_windows.no_rows", "There are no rows.");
        }

        if (rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != rows.Count)
        {
            return Fail("entry_windows.duplicate_key", "A trade key appears twice.");
        }

        if (rows.Any(r => r.Label is not (0 or 1)))
        {
            return Fail("entry_windows.bad_label", "A label is 0 or 1.");
        }

        if (validation.HoldOutStart is { } hold && rows.Any(r => r.TradeDate >= hold))
        {
            return Fail("entry_windows.holdout_touched", $"A row is on or after the hold-out start {hold:yyyy-MM-dd}; the hold-out is not used in development.");
        }

        var ordered = rows.OrderBy(r => r.TradeDate).ThenBy(r => r.DecisionEpochSeconds).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var from = ordered[0].TradeDate;
        var to = ordered[^1].TradeDate;
        var folds = SharedFolds(ordered, validation);
        var bucketCount = (SessionClock.RegularBars + options.BucketMinutes - 1) / options.BucketMinutes;
        var items = ordered.Select(r => new Item(r, BucketOf(r, options))).ToArray();
        var cells = items.GroupBy(i => i.Row.CellKey, StringComparer.Ordinal).Where(g => g.Any(i => i.Bucket >= 0)).OrderBy(g => g.Key, StringComparer.Ordinal);
        var proposals = cells.Select(g => Propose(g.Key, [.. g.Where(i => i.Bucket >= 0)], g.Count(i => i.Bucket < 0), folds, options, validation, bucketCount)).ToList();
        var outside = items.Count(i => i.Bucket < 0);
        return Result.Success(new EntryWindowReport(options, validation, ordered.Length, from, to, folds.Count, outside, proposals, LimitsOf(options, validation, ordered.Length, from, to, folds.Count, outside, proposals.Count(p => p.WindowStartMinute is not null))));
    }

    private static List<SharedFold> SharedFolds(ValidationRow[] ordered, ValidationOptions validation)
    {
        var (windows, _) = ExitSelector.Windows(ordered[0].TradeDate, ordered[^1].TradeDate, new SelectionOptions(validation.MinTrainMonths, validation.TestMonths));
        var folds = new List<SharedFold>();
        for (var i = 0; i < windows.Count; i++)
        {
            var (start, end) = windows[i];
            var test = ordered.Where(r => r.TradeDate >= start && r.TradeDate <= end).ToArray();
            if (test.Length == 0)
            {
                continue;
            }

            var firstDecision = test.Min(r => r.DecisionEpochSeconds);
            var trainCount = ordered.Count(r => Trains(r, start, firstDecision, validation));
            if (trainCount >= validation.MinTrainTrades)
            {
                folds.Add(new SharedFold(i + 1, start, end, firstDecision));
            }
        }

        return folds;
    }

    private static bool Trains(ValidationRow r, DateOnly start, long firstDecision, ValidationOptions validation) =>
        r.TradeDate < start.AddDays(-validation.EmbargoDays) && r.ResolvedEpochSeconds + HoldOut.BarSeconds < firstDecision;

    private static int BucketOf(ValidationRow row, EntryWindowOptions options)
    {
        var minute = SessionClock.MinuteOfDay(row.DecisionEpochSeconds - HoldOut.BarSeconds);
        return SessionClock.InRegularSession(minute) ? (minute - SessionClock.OpenMinute) / options.BucketMinutes : -1;
    }

    private static EntryWindowProposal Propose(string cellKey, Item[] cell, int dropped, List<SharedFold> folds, EntryWindowOptions o, ValidationOptions v, int bucketCount)
    {
        var reasons = new List<string>();
        var c = CultureInfo.InvariantCulture;
        var wins = cell.Count(i => i.Row.Label == 1);
        var rate = wins / (double)cell.Length;
        var buckets = Buckets(cell, o, bucketCount, rate);
        var choice = FindWindow(cell, o, bucketCount);
        if (choice is null)
        {
            var status = cell.Length < o.MinWindowTrades ? EntryWindowStatus.NotEnoughEvidence : EntryWindowStatus.NoStableWindow;
            reasons.Add(status == EntryWindowStatus.NotEnoughEvidence
                ? string.Create(c, $"Only {cell.Length} trades in the regular session ({dropped} dropped as decided out of session); a window needs {o.MinWindowTrades}.")
                : string.Create(c, $"No run of buckets has {o.MinWindowTrades} trades, a lift above {o.MinLift:F3} and no weak bucket inside."));
            return new EntryWindowProposal(cellKey, status, cell.Length, rate, null, null, 0, 0, null, null, null, null, 0, false, 0, 0, 0, null, 0, 0, [], buckets, reasons);
        }

        var inside = cell.Where(i => i.Bucket >= choice.I && i.Bucket <= choice.J).ToArray();
        var outsideItems = cell.Where(i => i.Bucket < choice.I || i.Bucket > choice.J).ToArray();
        var meanIn = inside.Average(i => i.Row.R);
        double? meanOut = outsideItems.Length > 0 ? outsideItems.Average(i => i.Row.R) : null;
        var kept = inside.Length / (double)cell.Length;
        var majority = kept < 0.5;
        var foldResults = folds.Select(f => EvaluateFold(f, cell, o, v, bucketCount)).ToList();
        var evaluable = foldResults.Count(f => f.Held is not null);
        var held = foldResults.Count(f => f.Held == true);
        var required = (int)Math.Ceiling(v.FoldsBetterShare * evaluable);
        var (pooledIn, pooledOut) = Pooled(foldResults);
        double? pooledLift = pooledIn is { } pi && pooledOut is { } po ? pi - po : null;
        var overlap = foldResults.Count(f => f.WindowStartMinute is { } fs && f.WindowEndMinute is { } fe && fs < BucketEnd(choice.J, o) && fe > BucketStart(choice.I, o));
        var equal = foldResults.Count(f => f.WindowStartMinute == BucketStart(choice.I, o) && f.WindowEndMinute == BucketEnd(choice.J, o));
        var status2 = Judge(evaluable, held, required, folds.Count, pooledLift, o, v, reasons);
        if (dropped > 0)
        {
            reasons.Add(string.Create(c, $"{dropped} trade(s) of this cell dropped as decided out of session."));
        }

        reasons.Add(string.Create(c, $"The folds judge the procedure (the window is re-learnt on each fold's train rows), not this exact window: {overlap} of {foldResults.Count} fold windows overlap it, {equal} equal it."));
        if (majority)
        {
            reasons.Add(string.Create(c, $"The window drops {1 - kept:P0} of the cell's trades (more than half): flagged."));
        }

        var start = BucketStart(choice.I, o);
        var end = BucketEnd(choice.J, o);
        return new EntryWindowProposal(
            cellKey, status2, cell.Length, rate, start, end, inside.Length, choice.Wins, choice.Shrunk, choice.Shrunk - rate, meanIn, meanOut, kept, majority, evaluable, held, required, pooledLift, overlap, equal, foldResults, buckets, reasons);
    }

    private static EntryWindowStatus Judge(int evaluable, int held, int required, int usedFolds, double? pooledLift, EntryWindowOptions o, ValidationOptions v, List<string> reasons)
    {
        var c = CultureInfo.InvariantCulture;
        if (evaluable < o.MinEvaluableFolds)
        {
            reasons.Add(string.Create(c, $"Only {evaluable} fold(s) could judge the window; {o.MinEvaluableFolds} needed."));
            return EntryWindowStatus.Inconclusive;
        }

        if (held < required)
        {
            reasons.Add(string.Create(c, $"The window held in {held} of {evaluable} folds; {required} needed."));
            return EntryWindowStatus.NoStableWindow;
        }

        if (pooledLift is not { } pl || pl <= o.MinLift)
        {
            reasons.Add(string.Create(c, $"Pooled out-of-sample lift {pooledLift:F3} is not above {o.MinLift:F3}."));
            return EntryWindowStatus.NoStableWindow;
        }

        if (usedFolds < v.MinFoldsForVerdict)
        {
            reasons.Add(string.Create(c, $"Held in {held} of {evaluable} folds, but fewer than {v.MinFoldsForVerdict} folds exist ({usedFolds}): at best inconclusive."));
            return EntryWindowStatus.Inconclusive;
        }

        reasons.Add(string.Create(c, $"Held in {held} of {evaluable} folds ({required} needed)."));
        return EntryWindowStatus.Proposed;
    }

    private static (double? In, double? Out) Pooled(List<EntryWindowFold> folds)
    {
        var judged = folds.Where(f => f.Held is not null).ToArray();
        var inN = judged.Sum(f => f.InTrades);
        var outN = judged.Sum(f => f.OutTrades);
        if (inN == 0 || outN == 0)
        {
            return (null, null);
        }

        return (judged.Sum(f => f.InRate!.Value * f.InTrades) / inN, judged.Sum(f => f.OutRate!.Value * f.OutTrades) / outN);
    }

    private static EntryWindowFold EvaluateFold(SharedFold f, Item[] cell, EntryWindowOptions o, ValidationOptions v, int bucketCount)
    {
        var train = cell.Where(i => Trains(i.Row, f.Start, f.FirstDecision, v)).ToArray();
        var test = cell.Where(i => i.Row.TradeDate >= f.Start && i.Row.TradeDate <= f.End).ToArray();
        var choice = FindWindow(train, o, bucketCount);
        if (choice is null)
        {
            return new EntryWindowFold(f.Index, f.Start, f.End, train.Length, null, null, 0, null, 0, null, null, "no window learnt on the train rows");
        }

        var inside = test.Where(i => i.Bucket >= choice.I && i.Bucket <= choice.J).ToArray();
        var outside = test.Length - inside.Length;
        var start = BucketStart(choice.I, o);
        var end = BucketEnd(choice.J, o);
        var inRate = inside.Length > 0 ? inside.Count(i => i.Row.Label == 1) / (double)inside.Length : (double?)null;
        var outRate = outside > 0 ? test.Where(i => i.Bucket < choice.I || i.Bucket > choice.J).Count(i => i.Row.Label == 1) / (double)outside : (double?)null;
        if (inside.Length < o.MinFoldTrades || outside < o.MinFoldTrades)
        {
            return new EntryWindowFold(f.Index, f.Start, f.End, train.Length, start, end, inside.Length, inRate, outside, outRate, null, string.Create(CultureInfo.InvariantCulture, $"fewer than {o.MinFoldTrades} test trades inside or outside the window"));
        }

        return new EntryWindowFold(f.Index, f.Start, f.End, train.Length, start, end, inside.Length, inRate, outside, outRate, inRate - outRate > o.MinLift, null);
    }

    private static Choice? FindWindow(Item[] items, EntryWindowOptions o, int bucketCount)
    {
        if (items.Length < o.MinWindowTrades)
        {
            return null;
        }

        var n = new int[bucketCount];
        var w = new int[bucketCount];
        foreach (var i in items)
        {
            n[i.Bucket]++;
            w[i.Bucket] += i.Row.Label;
        }

        var totalWins = w.Sum();
        var p0 = totalWins / (double)items.Length;
        Choice? best = null;
        for (var i = 0; i < bucketCount; i++)
        {
            if (n[i] == 0)
            {
                continue;
            }

            int sn = 0, sw = 0;
            for (var j = i; j < bucketCount; j++)
            {
                if (Shrink(w[j], n[j], p0, o.PriorStrength) < p0 && n[j] > 0)
                {
                    break; // a weak bucket inside: this window and every longer one has a hole
                }

                sn += n[j];
                sw += w[j];
                if (n[j] == 0 || sn < o.MinWindowTrades)
                {
                    continue;
                }

                var score = Shrink(sw, sn, p0, o.PriorStrength);
                if (score - p0 <= o.MinLift)
                {
                    continue;
                }

                if (best is null || score > best.Shrunk || (score == best.Shrunk && sn > best.N))
                {
                    best = new Choice(i, j, sn, sw, score);
                }
            }
        }

        return best;
    }

    private static double Shrink(int wins, int n, double centre, double strength) => n + strength <= 0 ? centre : (wins + (strength * centre)) / (n + strength);

    private static List<EntryWindowBucket> Buckets(Item[] cell, EntryWindowOptions o, int bucketCount, double rate)
    {
        var list = new List<EntryWindowBucket>(bucketCount);
        for (var b = 0; b < bucketCount; b++)
        {
            var inB = cell.Where(i => i.Bucket == b).ToArray();
            var w = inB.Count(i => i.Row.Label == 1);
            list.Add(new EntryWindowBucket(BucketStart(b, o), BucketEnd(b, o), inB.Length, w, inB.Length > 0 ? inB.Average(i => i.Row.R) : null, Shrink(w, inB.Length, rate, o.PriorStrength)));
        }

        return list;
    }

    private static int BucketStart(int bucket, EntryWindowOptions o) => SessionClock.OpenMinute + (bucket * o.BucketMinutes);

    private static int BucketEnd(int bucket, EntryWindowOptions o) => Math.Min(SessionClock.OpenMinute + ((bucket + 1) * o.BucketMinutes), SessionClock.CloseMinute);

    private static List<string> LimitsOf(EntryWindowOptions o, ValidationOptions v, int rows, DateOnly from, DateOnly to, int folds, int outside, int compared)
    {
        var c = CultureInfo.InvariantCulture;
        var limits = new List<string>
        {
            string.Create(c, $"Replayed history, modelled fills, in-sample on the data shown ({from:yyyy-MM-dd} to {to:yyyy-MM-dd}, {rows} trades, {folds} folds): a description of the past, not a forecast."),
            "A window is a hypothesis for the hypothesis loop (P6-10); nothing is applied to a strategy or the scanner.",
            string.Create(c, $"Windows are runs of {o.BucketMinutes}-minute buckets; many windows are compared per cell, so the best one is optimistic by selection; the folds are the check, the lift of {o.MinLift:F3} and the prior of {o.PriorStrength:F0} are the guards."),
        };
        var alpha = FalseProposalChance(folds, v.FoldsBetterShare);
        limits.Add(string.Create(c, $"{compared} cells compared, expect about {alpha * compared:F2} false proposals (alpha {alpha:F3}: chance that coin-flip folds give {Math.Ceiling(v.FoldsBetterShare * folds):F0} of {folds} holds; the lift floor makes it smaller)."));
        limits.Add("Fold windows are bar minutes of evaluation (decision minus 60 s); feature 32 (minute_of_day) is the decision minute, one later.");
        if (folds < v.MinFoldsForVerdict)
        {
            limits.Add(string.Create(c, $"Fewer than {v.MinFoldsForVerdict} folds: no window can be called proposed, at best inconclusive."));
        }

        if (outside > 0)
        {
            limits.Add(string.Create(c, $"{outside} trade(s) decided outside the regular session were not bucketed."));
        }

        return limits;
    }

    private static double FalseProposalChance(int folds, double share)
    {
        if (folds == 0)
        {
            return 0;
        }

        var need = (int)Math.Ceiling(share * folds);
        double tail = 0, coef = 1;
        for (var k = 0; k <= folds; k++)
        {
            if (k > 0)
            {
                coef = coef * (folds - k + 1) / k;
            }

            if (k >= need)
            {
                tail += coef * Math.Pow(0.5, folds);
            }
        }

        return tail;
    }

    private static Result<EntryWindowReport> Fail(string code, string message) => Result.Failure<EntryWindowReport>(Error.Validation(code, message));

    private sealed record Item(ValidationRow Row, int Bucket);

    private sealed record SharedFold(int Index, DateOnly Start, DateOnly End, long FirstDecision);

    private sealed record Choice(int I, int J, int N, int Wins, double Shrunk);
}
