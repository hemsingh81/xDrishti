using System.Globalization;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// Protocol F of P15-10: does the event-risk filter (results within N sessions) remove trades that are worse than the ones it keeps, without emptying the book? The population is the trades of unfiltered runs, each classified at its decision instant as skipped, taken or unavailable
/// (the filter fails closed: ADR 0182). KEEP needs, all of: at least 100 skipped trades; the mean of the skipped below the taken with a one-sided day-block lower bound of the gap at or above 0.03 R times the factor, <b>or</b> a skipped tail (R at or below -1.5) share at least twice the taken
/// one; at most half of all trades removed when ON (skipped plus all unavailable); the same direction in two thirds of the seasons that have both sets. A thin population is NOT_EVALUABLE with the missing quantity named. Pure and seeded: the order of the trades does not matter.
/// </summary>
public static class ProtocolF
{
    /// <summary>The protocol letter.</summary>
    public const string Protocol = "F";

    /// <summary>The window sizes that are counted variants.</summary>
    public static IReadOnlyList<int> Variants { get; } = [1, 2, 3];

    private static readonly string[] KnownReasons = [ResultsAnswer.NoEntry, ResultsAnswer.OnlyCancelled, ResultsAnswer.NoUpcomingEntry];

    /// <summary>Splits trades into the development set and the hold-out (the last four seasons with data); with no more than four seasons the development set is empty.</summary>
    public static (IReadOnlyList<FilterTrade> Development, IReadOnlyList<FilterTrade> HoldOut, int Seasons) Split(IReadOnlyList<FilterTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var seasons = SeasonFolds.Distinct(trades.Select(t => t.Date));
        var start = SeasonFolds.HoldOutStart(seasons);
        return start is null
            ? ([], [.. trades], seasons.Count)
            : (trades.Where(t => t.Date < start).ToArray(), trades.Where(t => t.Date >= start).ToArray(), seasons.Count);
    }

    /// <summary>The verdict for window <paramref name="days"/> over the development trades; <paramref name="variants"/> is N of the ledger; <paramref name="seasonsWithData"/> counts all seasons including the reserved four.</summary>
    public static Result<ProtocolFResult> Evaluate(int days, int variants, int seasonsWithData, IReadOnlyList<FilterTrade> trades, ProtocolFOptions options)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Problems().ToList();
        if (!Variants.Contains(days))
        {
            problems.Add("the window is 1, 2 or 3 sessions");
        }

        if (variants < 1)
        {
            problems.Add("the ledger count is at least 1");
        }

        if (trades.Any(t => t is null || !double.IsFinite(t.R)))
        {
            problems.Add("every trade needs a finite R");
        }

        if (problems.Count > 0)
        {
            return Result.Failure<ProtocolFResult>(Error.Validation("extensions.options", string.Join("; ", problems)));
        }

        var c = CultureInfo.InvariantCulture;
        var ordered = trades.OrderBy(t => t.Date).ThenBy(t => t.Key, StringComparer.Ordinal).ToArray();
        var skipped = ordered.Where(t => t.Fate == FilterFate.Skipped).ToArray();
        var taken = ordered.Where(t => t.Fate == FilterFate.Taken).ToArray();
        var unavailable = ordered.Where(t => t.Fate == FilterFate.Unavailable).ToArray();
        var byReason = unavailable.GroupBy(t => t.Reason is not null && KnownReasons.Contains(t.Reason, StringComparer.Ordinal) ? t.Reason : "other").ToDictionary(g => g.Key, g => g.Count());
        var total = ordered.Length;
        var factor = MultipleTesting.Factor(variants);
        var gain = options.MinMeanGapR * factor;
        var seasons = SeasonFolds.Distinct(ordered.Select(t => t.Date));
        string[] limits =
        [
            "Gross R from the filled prices; no charges are taken off unless the owner's checked charge sheet exists (ISS-25).",
            "Seasons, not trades, are the independent units; the intervals are wide.",
            "Selection bias: the stored runs are the strategies that were chosen to be run, not all possible entries; the same instrument and day can be a trade of several strategies, so the trades are correlated and the counts overstate their independence.",
            "The tail test compares Wilson intervals and is not widened by the multiple-testing factor.",
            "Advisory research only: a KEEP is a record; it changes nothing unless the owner has set approval to auto and the hold-out confirmed the KEEP.",
        ];

        var missing = new List<string>();
        if (seasonsWithData <= SeasonFolds.HoldOutSeasons)
        {
            missing.Add(string.Create(c, $"seasons {seasonsWithData} of {SeasonFolds.HoldOutSeasons + 1} (the last {SeasonFolds.HoldOutSeasons} are reserved as the hold-out)"));
        }
        else if (total == 0)
        {
            missing.Add("trades 0");
        }

        if (total > 0 && skipped.Length < options.MinSkipped)
        {
            missing.Add(string.Create(c, $"skipped trades {skipped.Length} of {options.MinSkipped}"));
        }

        if (total > 0 && taken.Length == 0)
        {
            missing.Add("taken trades 0");
        }

        var removed = skipped.Length + unavailable.Length;
        var removalPass = total > 0 && removed <= (options.MaxRemovedShare * total) + 1e-9;
        var skippedTail = skipped.Count(t => t.R <= options.TailR);
        var takenTail = taken.Count(t => t.R <= options.TailR);
        double? skippedShare = skipped.Length > 0 ? skippedTail / (double)skipped.Length : null;
        double? takenShare = taken.Length > 0 ? takenTail / (double)taken.Length : null;
        var (skippedLow, _) = skipped.Length > 0 ? Wilson.Interval(skippedTail, skipped.Length) : (double.NaN, double.NaN);
        var (_, takenHigh) = taken.Length > 0 ? Wilson.Interval(takenTail, taken.Length) : (double.NaN, double.NaN);

        // The tail test compares the intervals, not the point shares: the lower Wilson bound of the skipped share must reach the ratio times the upper bound of the taken share. It is not widened by the factor (ADR 0186, known limit).
        var tailPass = missing.Count == 0 && skippedTail >= 1 && skippedLow >= options.TailRatio * takenHigh;
        double? meanSkipped = skipped.Length > 0 ? skipped.Sum(t => t.R) / skipped.Length : null;
        double? meanTaken = taken.Length > 0 ? taken.Sum(t => t.R) / taken.Length : null;
        var judgedSeasons = total > 0 ? ordered.GroupBy(t => Season.Of(t.Date)).Count(g => g.Any(t => t.Fate == FilterFate.Skipped) && g.Any(t => t.Fate == FilterFate.Taken)) : 0;
        if (total > 0 && skipped.Length >= options.MinSkipped && taken.Length > 0 && judgedSeasons < options.MinJudgedSeasons)
        {
            missing.Add(string.Create(c, $"judged seasons {judgedSeasons} of {options.MinJudgedSeasons}"));
        }

        var bounds = missing.Count == 0 ? ClusterBootstrap.Difference(DayBlocks(skipped, taken), options.Draws, options.Seed, options.Level) : null;
        var meanPass = bounds?.Lower is { } lower && lower >= gain;
        var basis = meanPass ? "mean" : tailPass ? "tail" : "mean";
        var folds = missing.Count == 0 ? SeasonVotes(ordered, basis, options.TailR) : [];
        var judged = folds.Count(f => f.Judged);
        var agree = folds.Count(f => f.Agrees);
        var needed = (int)Math.Ceiling((judged * options.SameSignShare) - 1e-9);
        var foldsPass = judged > 0 && agree >= needed;

        var failed = new List<string>();
        var verdict = ExtensionVerdict.NotEvaluable;
        var unrepairable = false;
        if (missing.Count == 0)
        {
            if (!meanPass && !tailPass)
            {
                failed.Add(string.Create(c, $"neither test passed: mean gap lower bound {Fmt(bounds!.Lower)} R below {gain:F3} R, and the lower bound of the skipped tail share ({Share(skippedLow)}) is not {options.TailRatio:F1} times the upper bound of the taken ({Share(takenHigh)})"));
            }

            if (!removalPass)
            {
                failed.Add(string.Create(c, $"the filter removes {100.0 * removed / total:F1} % of the trades when ON (skipped {skipped.Length} plus unavailable {unavailable.Length} of {total}), above {100 * options.MaxRemovedShare:F0} %"));
            }

            if (!foldsPass)
            {
                failed.Add(judged == 0 ? "no season has both skipped and taken trades" : string.Create(c, $"same direction in {agree} of {judged} seasons, {needed} needed"));
            }

            unrepairable = !removalPass || (!meanPass && !tailPass && bounds!.Upper is { } upper && upper < options.MinMeanGapR);
            verdict = VerdictTable.Decide(missing, failed, unrepairable);
        }

        using var estimates = new ExtensionJson();
        estimates.Object().Text("window_sessions", days.ToString(c)).Count("trades", total).Count("skipped", skipped.Length).Count("taken", taken.Length).Count("unavailable", unavailable.Length)
            .Number("skipped_share", Ratio(skipped.Length, total)).Number("taken_share", Ratio(taken.Length, total)).Number("unavailable_share", Ratio(unavailable.Length, total)).Number("removed_share", Ratio(removed, total))
            .Number("share_lost_to_no_upcoming_entry", Ratio(byReason.GetValueOrDefault(ResultsAnswer.NoUpcomingEntry), total));
        estimates.Object("unavailable_by_reason");
        foreach (var reason in KnownReasons.Concat(byReason.Keys.Except(KnownReasons, StringComparer.Ordinal).Order(StringComparer.Ordinal)))
        {
            estimates.Object(reason).Count("trades", byReason.GetValueOrDefault(reason)).Number("share", Ratio(byReason.GetValueOrDefault(reason), total)).EndObject();
        }

        estimates.EndObject();
        Tail(estimates, "tail_skipped", skippedTail, skipped.Length);
        Tail(estimates, "tail_taken", takenTail, taken.Length);
        estimates.Number("mean_r_skipped", meanSkipped).Number("mean_r_taken", meanTaken).Number("mean_gap_r", bounds?.Point).Number("mean_gap_lower", bounds?.Lower).Number("mean_gap_upper", bounds?.Upper)
            .Number("mean_gap_required", missing.Count == 0 ? gain : null).Text("basis", missing.Count == 0 ? basis : null).Count("seasons_agree", agree).Count("seasons_judged", judged).EndObject();

        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol).Text("rule_version", EventCalendarSnapshot.RuleVersion).Count("ledger_trials", variants).Number("factor", factor).Array("seasons");
        foreach (var f in folds)
        {
            evidence.Object().Text("season", f.Season.ToString()).Count("skipped", f.Skipped).Count("taken", f.Taken).Flag("judged", f.Judged).Flag("agrees", f.Agrees).EndObject();
        }

        evidence.EndArray();
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        var outcome = new ProtocolOutcome(verdict, missing, failed, limits, estimates.Finish(), evidence.Finish(), seasons.Count, judged, total);
        return Result.Success(new ProtocolFResult(outcome, basis));
    }

    /// <summary>The one reading of the hold-out: KEEP when the direction of the development test (<paramref name="basis"/>) holds on the reserved seasons and the filter still removes at most half; INCONCLUSIVE when not; NOT_EVALUABLE with too few skipped or no taken trades.</summary>
    public static Result<ProtocolOutcome> HoldOut(int days, IReadOnlyList<FilterTrade> trades, string basis, ProtocolFOptions options)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(options);
        if (!Variants.Contains(days) || basis is not ("mean" or "tail") || options.Problems().Count > 0 || trades.Any(t => !double.IsFinite(t.R)))
        {
            return Result.Failure<ProtocolOutcome>(Error.Validation("extensions.options", "The hold-out needs a window of 1 to 3 sessions, the basis mean or tail, valid options and finite R."));
        }

        var c = CultureInfo.InvariantCulture;
        var skipped = trades.Where(t => t.Fate == FilterFate.Skipped).ToArray();
        var taken = trades.Where(t => t.Fate == FilterFate.Taken).ToArray();
        var unavailable = trades.Count(t => t.Fate == FilterFate.Unavailable);
        var missing = new List<string>();
        if (skipped.Length < options.MinHoldOutSkipped)
        {
            missing.Add(string.Create(c, $"hold-out skipped trades {skipped.Length} of {options.MinHoldOutSkipped}"));
        }

        if (taken.Length < options.MinHoldOutTaken)
        {
            missing.Add(string.Create(c, $"hold-out taken trades {taken.Length} of {options.MinHoldOutTaken}"));
        }

        var failed = new List<string>();
        double? meanSkipped = skipped.Length > 0 ? skipped.Sum(t => t.R) / skipped.Length : null;
        double? meanTaken = taken.Length > 0 ? taken.Sum(t => t.R) / taken.Length : null;
        double? skippedShare = skipped.Length > 0 ? skipped.Count(t => t.R <= options.TailR) / (double)skipped.Length : null;
        double? takenShare = taken.Length > 0 ? taken.Count(t => t.R <= options.TailR) / (double)taken.Length : null;
        var removed = skipped.Length + unavailable;
        if (missing.Count == 0)
        {
            // The hold-out must show the same effect with its own interval: a one-sided day-block lower bound above zero for the mean gap, or the Wilson interval of the skipped tail share clear of the taken one for the tail.
            var bounds = ClusterBootstrap.Difference(DayBlocks(skipped, taken), options.Draws, options.Seed, options.Level);
            var (skippedLow, _) = Wilson.Interval(skipped.Count(t => t.R <= options.TailR), skipped.Length);
            var (_, takenHigh) = Wilson.Interval(taken.Count(t => t.R <= options.TailR), taken.Length);
            var same = basis == "mean" ? bounds.Lower > 0 : skippedLow > takenHigh;
            if (!same)
            {
                failed.Add(basis == "mean" ? "the lower bound of the mean gap is not above zero on the hold-out" : "the skipped tail share is not clear of the taken one on the hold-out");
            }

            if (removed > (options.MaxRemovedShare * trades.Count) + 1e-9)
            {
                failed.Add(string.Create(c, $"the filter removes {100.0 * removed / trades.Count:F1} % of the hold-out trades when ON"));
            }
        }

        var verdict = missing.Count > 0 ? ExtensionVerdict.NotEvaluable : failed.Count == 0 ? ExtensionVerdict.Keep : ExtensionVerdict.Inconclusive;
        using var estimates = new ExtensionJson();
        estimates.Object().Text("window_sessions", days.ToString(c)).Count("hold_out_trades", trades.Count).Count("skipped", skipped.Length).Count("taken", taken.Length).Count("unavailable", unavailable)
            .Number("removed_share", Ratio(removed, trades.Count)).Number("mean_r_skipped", meanSkipped).Number("mean_r_taken", meanTaken).Number("tail_share_skipped", skippedShare).Number("tail_share_taken", takenShare).Text("basis", basis).EndObject();
        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol).Text("read", "hold-out, once");
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        return Result.Success(new ProtocolOutcome(verdict, missing, failed, ["The hold-out is read once; this record is that reading.", "Advisory research only: a KEEP is a record."], estimates.Finish(), evidence.Finish(), SeasonFolds.Distinct(trades.Select(t => t.Date)).Count, 0, trades.Count));
    }

    private sealed record SeasonVote(Season Season, int Skipped, int Taken, bool Judged, bool Agrees);

    private static List<SeasonVote> SeasonVotes(FilterTrade[] ordered, string basis, double tailR)
    {
        var votes = new List<SeasonVote>();
        foreach (var group in ordered.GroupBy(t => Season.Of(t.Date)).OrderBy(g => g.Key))
        {
            var s = group.Where(t => t.Fate == FilterFate.Skipped).ToArray();
            var k = group.Where(t => t.Fate == FilterFate.Taken).ToArray();
            var judged = s.Length > 0 && k.Length > 0;
            var agrees = judged && (basis == "mean"
                ? (k.Sum(t => t.R) / k.Length) > (s.Sum(t => t.R) / s.Length)
                : (s.Count(t => t.R <= tailR) / (double)s.Length) > (k.Count(t => t.R <= tailR) / (double)k.Length));
            votes.Add(new SeasonVote(group.Key, s.Length, k.Length, judged, agrees));
        }

        return votes;
    }

    private static List<DifferenceBlock> DayBlocks(FilterTrade[] skipped, FilterTrade[] taken)
    {
        var byDate = new SortedDictionary<DateOnly, (double SumA, int CountA, double SumB, int CountB)>();
        foreach (var t in taken)
        {
            var b = byDate.GetValueOrDefault(t.Date);
            byDate[t.Date] = (b.SumA + t.R, b.CountA + 1, b.SumB, b.CountB);
        }

        foreach (var t in skipped)
        {
            var b = byDate.GetValueOrDefault(t.Date);
            byDate[t.Date] = (b.SumA, b.CountA, b.SumB + t.R, b.CountB + 1);
        }

        return [.. byDate.Values.Select(b => new DifferenceBlock(b.SumA, b.CountA, b.SumB, b.CountB))];
    }

    private static void Tail(ExtensionJson json, string name, int k, int n)
    {
        var (low, high) = n > 0 ? Wilson.Interval(k, n) : (double.NaN, double.NaN);
        json.Object(name).Count("tail_trades", k).Count("trades", n).Number("share", n > 0 ? k / (double)n : null).Number("wilson_low", low).Number("wilson_high", high).EndObject();
    }

    private static double? Ratio(int part, int whole) => whole > 0 ? part / (double)whole : null;

    private static string Fmt(double? v) => v is { } x ? x.ToString("F4", CultureInfo.InvariantCulture) : "unavailable";

    private static string Share(double v) => double.IsFinite(v) ? (100 * v).ToString("F1", CultureInfo.InvariantCulture) + " %" : "unavailable";

    private static string Share(double? v) => v is { } x ? (100 * x).ToString("F1", CultureInfo.InvariantCulture) + " %" : "unavailable";
}
