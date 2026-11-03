using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>
/// Protocol O of P14-09 (option expression versus cash, family <c>expr:option_buy</c>): the verdict over the paired trades of the out-of-sample windows. The unit is the paired difference <c>d = R_equiv(option) - R_equiv(cash)</c> on the same rupee risk; the folds are calendar quarters (no model is fitted, so a quarter with at least 20 paired
/// trades is evaluable); the bounds are one-sided 90 % block-bootstrap bounds of the mean <c>d</c> by day blocks and, as the stated sensitivity, by 5-session blocks (10,000 draws, seed 1337). NOT_EVALUABLE names every missing quantity (folds, paired trades, the half-spread, a bound, no pair at all).
/// KEEP needs all of: the option's own OOS evidence through <see cref="VerdictEvaluator"/> with N; both lower bounds at or above <c>0.05 R x F</c>; a positive mean <c>d</c> in two thirds of the evaluable folds and in the latest rule epoch (30 pairs or more, else unavailable and not met); net of the owner's charge lines; a Black-76 fallback share of 5 % or less.
/// DROP needs the minima, not KEEP, net, and the larger upper bound below <c>0.05 R</c> (not widened). Every gross result is INCONCLUSIVE, whichever way it points. The last six months are the hold-out: <see cref="Evaluate"/> never sees them; <see cref="HoldOut"/> is the one reading. An unavailable input never passes a rule and never supports DROP. Pure and seeded.
/// </summary>
public static class ProtocolO
{
    /// <summary>The protocol letter.</summary>
    public const string Protocol = ExpressionFamilies.Protocol;

    /// <summary>The fate word of a trade whose net figure had no half-spread (P14-08, ADR 0197).</summary>
    public const string SpreadUnavailable = "spread_unavailable";

    /// <summary>The name of the missing quantity when there is no paired trade at all.</summary>
    public const string NoPairedTrades = "no_paired_trades";

    private sealed record Day(DateOnly Date, double Sum, int Count);

    private sealed record Fold(DateOnly Start, int N, double MeanD, double MeanOption, bool Evaluable);

    /// <summary>The first day of the hold-out: six months before the last date, plus a day (the cut of <c>HoldOut.Reserve</c>).</summary>
    public static DateOnly HoldOutStartOf(DateOnly lastDate, ExpressionStudyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return lastDate.AddMonths(-options.HoldOutMonths).AddDays(1);
    }

    /// <summary>
    /// Splits the pairs into development and hold-out by date. The development pairs dated before <paramref name="holdOutStart"/> lose the pairs of the last five trade dates before it (the embargo) and the ones that leave on or after it (the purge): both counted. With no hold-out start everything is development.
    /// The result is in a canonical order (entry instant, then trade key), so sums do not depend on the order the rows were read in.
    /// </summary>
    public static (IReadOnlyList<ExpressionPair> Development, int Dropped) Cut(IReadOnlyList<ExpressionPair> pairs, ExpressionStudyOptions options, DateOnly? holdOutStart)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(options);
        var ordered = pairs.OrderBy(p => p.EntryEpochSeconds).ThenBy(p => p.TradeKey, StringComparer.Ordinal).ToList();
        if (holdOutStart is not { } start)
        {
            return (ordered, 0);
        }

        var before = ordered.Where(p => p.Date < start).ToList();
        var embargoed = before.Select(p => p.Date).Distinct().OrderDescending().Take(options.EmbargoSessions).ToHashSet();
        var kept = before.Where(p => !embargoed.Contains(p.Date) && IndiaTime.Today(DateTimeOffset.FromUnixTimeSeconds(p.ExitEpochSeconds)) < start).ToList();
        // shortcut: the embargo counts trade dates of the paired trades, not exchange sessions (a date with no paired trade is not counted), and so does the 5-session block of the sensitivity: both are conservative or equal for a dense book and wider in calendar time for a sparse one.
        return (kept, before.Count - kept.Count);
    }

    /// <summary>The development verdict.</summary>
    public static Result<ProtocolOResult> Evaluate(ProtocolOInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var o = input.Options;
        if (input.Trials < 1 || !o.IsValid || input.Pairs.Any(p => !double.IsFinite(p.OptionR) || !double.IsFinite(p.CashR)))
        {
            return Result.Failure<ProtocolOResult>(Error.Validation("extensions.options", "Protocol O needs the ledger count (at least 1), valid options and a finite R_equiv for every paired trade."));
        }

        var c = CultureInfo.InvariantCulture;
        var (dev, dropped) = Cut(input.Pairs, o, input.HoldOutStart);
        var gross = dev.Any(p => !p.Net);
        var factor = MultipleTesting.Factor(input.Trials);
        var required = o.MarginR * factor;
        var folds = dev.GroupBy(p => QuarterStart(p.Date)).OrderBy(g => g.Key)
            .Select(g => new Fold(g.Key, g.Count(), g.Average(p => p.D), g.Average(p => p.OptionR), g.Count() >= o.MinPairedPerFold)).ToList();
        var evaluable = folds.Where(f => f.Evaluable).ToList();
        var pool = dev.Where(p => evaluable.Any(f => f.Start == QuarterStart(p.Date))).ToList();
        var unpairedTotal = input.Unpaired.Values.Sum();
        var spreadLost = input.Unpaired.TryGetValue(SpreadUnavailable, out var lost) ? lost : 0;

        var missing = new List<string>();
        var allInHoldOut = dev.Count == 0 && input.HoldOutStart is { } cut && input.LastPaired is { } lastPaired && lastPaired >= cut;
        if (dev.Count == 0 && !allInHoldOut)
        {
            missing.Add(NoPairedTrades + string.Create(c, $" ({unpairedTotal} out-of-sample trades, none paired)"));
            if (spreadLost > 0)
            {
                missing.Add(SpreadUnavailable + string.Create(c, $" ({spreadLost} of {unpairedTotal} unpaired trades have no half-spread)"));
            }
        }

        if (evaluable.Count < o.MinFolds)
        {
            missing.Add(string.Create(c, $"folds {evaluable.Count} of {o.MinFolds}"));
        }

        if (pool.Count < o.MinPaired)
        {
            missing.Add(allInHoldOut
                ? string.Create(c, $"paired trades 0 of {o.MinPaired} (every paired trade lies in the hold-out from {input.HoldOutStart:yyyy-MM-dd})")
                : string.Create(c, $"paired trades {pool.Count} of {o.MinPaired}"));
        }

        BootstrapBounds? dayBounds = null;
        BootstrapBounds? sessionBounds = null;
        if (missing.Count == 0)
        {
            var days = DayBlocks(pool, p => p.D);
            dayBounds = BlockBootstrap.Bounds([.. days.Select(d => new BootstrapBlock(d.Sum, d.Count))], o.Draws, o.Seed, o.Level);
            sessionBounds = BlockBootstrap.Bounds([.. SessionBlocks(days, o.BlockSessions).Select(d => new BootstrapBlock(d.Sum, d.Count))], o.Draws, o.Seed, o.Level);
            if (dayBounds.Lower is null || dayBounds.Upper is null)
            {
                missing.Add("day-block bound unavailable");
            }

            if (sessionBounds.Lower is null || sessionBounds.Upper is null)
            {
                missing.Add(string.Create(c, $"{o.BlockSessions}-session-block bound unavailable"));
            }
        }

        var evaluated = missing.Count == 0;
        var failed = new List<string>();
        var gateReasons = new List<string>();
        var positive = evaluable.Count(f => f.MeanD > 0);
        var needed = (int)Math.Ceiling((evaluable.Count * o.PositiveFoldShare) - 1e-9);
        double? meanD = pool.Count > 0 ? pool.Average(p => p.D) : null;
        double? meanOption = pool.Count > 0 ? pool.Average(p => p.OptionR) : null;
        double? meanCash = pool.Count > 0 ? pool.Average(p => p.CashR) : null;
        double? sd = pool.Count > 1 ? Sd(pool.Select(p => p.D).ToList(), meanD!.Value) : null;
        double? naiveSe = sd is { } s && pool.Count > 0 ? s / Math.Sqrt(pool.Count) : null;
        double? designEffect = evaluated && naiveSe is > 0 && dayBounds!.StandardError is { } se ? se / naiveSe : null;
        double? mde = naiveSe is { } n ? o.MdeMultiple * n : null;
        var latest = pool.Where(p => p.Date >= input.LatestEpochFrom).ToList();
        var earlier = pool.Where(p => p.Date < input.LatestEpochFrom).ToList();
        double? latestMean = latest.Count >= o.MinLatestEpochPaired ? latest.Average(p => p.D) : null;
        double? fallbackShare = pool.Count > 0 ? pool.Count(p => p.BsFallback) / (double)pool.Count : null;
        double? derivedShare = pool.Count > 0 ? pool.Count(p => p.ExpiryDerived) / (double)pool.Count : null;
        double? recentSpreadShare = pool.Count > 0 ? pool.Count(p => p.SpreadFromRecentArchive) / (double)pool.Count : null;
        var sizable = pool.Where(p => p.Sizable).ToList();
        BootstrapBounds? sizableBounds = evaluated && sizable.Count > 0 ? BlockBootstrap.Bounds([.. DayBlocks(sizable, p => p.D).Select(d => new BootstrapBlock(d.Sum, d.Count))], o.Draws, o.Seed, o.Level) : null;
        double? drawdown = null;
        var unrepairable = false;
        if (evaluated)
        {
            if (!(dayBounds!.Lower >= required))
            {
                failed.Add(string.Create(c, $"day-block lower bound {Fmt(dayBounds.Lower)} R below {required:F3} R"));
            }

            if (!(sessionBounds!.Lower >= required))
            {
                failed.Add(string.Create(c, $"{o.BlockSessions}-session-block lower bound {Fmt(sessionBounds.Lower)} R below {required:F3} R"));
            }

            if (positive < needed)
            {
                failed.Add(string.Create(c, $"mean d above zero in {positive} of {evaluable.Count} folds, {needed} needed"));
            }

            if (latest.Count < o.MinLatestEpochPaired)
            {
                failed.Add(string.Create(c, $"latest epoch n {latest.Count} of {o.MinLatestEpochPaired}"));
            }
            else if (!(latestMean > 0))
            {
                failed.Add(string.Create(c, $"mean d in the latest epoch is {Fmt(latestMean)} R (n {latest.Count}), not above 0"));
            }

            if (gross)
            {
                failed.Add("gross: the owner's option and cash charge lines were not both applied, so no result is a KEEP or a DROP");
            }

            if (!(pool.Count(p => p.BsFallback) <= (o.MaxFallbackShare * pool.Count) + 1e-9))
            {
                failed.Add(string.Create(c, $"Black-76 fallback share {fallbackShare * 100:F1} % above {o.MaxFallbackShare * 100:F0} %"));
            }

            drawdown = MaxDrawdown(pool);
            var wins = pool.Where(p => p.OptionR > 0).Sum(p => p.OptionR);
            var losses = -pool.Where(p => p.OptionR < 0).Sum(p => p.OptionR);
            var profitFactor = losses > 0 ? wins / losses : wins > 0 ? double.PositiveInfinity : 0;
            var ownPositive = evaluable.Count(f => f.MeanOption > 0);
            var lab = VerdictEvaluator.Evaluate(o.Gates, new OosEvidence(pool.Count, meanOption!.Value, profitFactor, evaluable.Count, ownPositive, drawdown.Value, 0, OosEvidence.WalkForward) /* calibration error: not applicable, 0 cannot fail the gate */, input.Trials, o.MinFolds);
            gateReasons.AddRange(lab.Gates.Select(g => g.Reason));
            if (lab.Outcome != VerdictOutcome.Pass)
            {
                failed.Add("the option's own out-of-sample evidence does not pass the Lab's gates: " + string.Join("; ", lab.Gates.Where(g => !g.Passed).Select(g => g.Name).Concat(lab.Limits)));
            }

            unrepairable = !gross && Math.Max(dayBounds.Upper!.Value, sessionBounds.Upper!.Value) < o.MarginR;
        }

        var verdict = VerdictTable.Decide(missing, failed, unrepairable);
        var limits = new List<string>
        {
            ExpressionWindows.Rule,
            "future baseline: NOT_EVALUABLE no_futures_bars (no futures bars exist)",
            "survivorship (ISS-22) and the recent-spread assumption flatter both sides unequally",
            "bar timestamp, IV unit, open-interest unit and the derived expiry weekdays are UNVERIFIED (ADR 0197); the fill is the next option bar's open, up to one bar after the cash fill",
            gross ? "costs: gross, the owner's charge lines were not applied to both sides; the result is INCONCLUSIVE whichever way it points" : "costs: net of the owner's option and cash charge lines and the half-spread",
            "Advisory research only: the expression stays cash on every decision path.",
        };
        var total = input.Pairs.Count + unpairedTotal;
        if (total > 0 && unpairedTotal > o.UnpairedLimitShare * total)
        {
            limits.Add(string.Create(c, $"unpaired share {unpairedTotal * 100.0 / total:F1} % above {o.UnpairedLimitShare * 100:F0} %: the paired remainder is a selected sample"));
        }

        if (evaluated && gross && Math.Max(dayBounds!.Upper!.Value, sessionBounds!.Upper!.Value) < o.MarginR)
        {
            limits.Add("gross upper bound below margin");
        }

        using var estimates = new ExtensionJson();
        estimates.Object().Count("paired", dev.Count).Count("pooled_paired", pool.Count).Count("folds", folds.Count).Count("evaluable_folds", evaluable.Count).Count("positive_folds", positive).Number("mean_d", meanD)
            .Number("mean_option_r", meanOption).Number("mean_cash_r", meanCash).Number("sd_d", sd).Number("naive_se", naiveSe).Number("design_effect", designEffect).Number("mde", mde)
            .Number("day_lower", dayBounds?.Lower).Number("day_upper", dayBounds?.Upper).Number("session_lower", sessionBounds?.Lower).Number("session_upper", sessionBounds?.Upper).Number("required", evaluated ? required : null)
            .Count("latest_epoch_n", latest.Count).Number("latest_epoch_mean_d", latestMean).Number("fallback_share", fallbackShare).Number("derived_expiry_share", derivedShare).Number("recent_spread_share", recentSpreadShare)
            .Count("sizable_n", sizable.Count).Number("sizable_mean_d", sizable.Count > 0 ? sizable.Average(p => p.D) : null).Number("sizable_day_lower", sizableBounds?.Lower).Number("max_drawdown_option_r", drawdown).Count("embargo_dropped", dropped).EndObject();

        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol).Count("ledger_trials", input.Trials).Number("factor", factor).Text("basis", dev.Count == 0 ? null : gross ? "gross" : "net").Text("calibration_gate", "not applicable (no probability)")
            .Text("hold_out_start", input.HoldOutStart?.ToString("yyyy-MM-dd", c)).Text("future_baseline", "NOT_EVALUABLE no_futures_bars").Text("oos_rule", ExpressionWindows.Rule).Object("unpaired").Count("total", unpairedTotal);
        foreach (var reason in input.Unpaired.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            evidence.Count(reason.Key, reason.Value);
        }

        evidence.EndObject().Array("folds");
        foreach (var f in folds)
        {
            evidence.Object().Text("quarter", QuarterLabel(f.Start)).Count("paired", f.N).Number("mean_d", f.MeanD).Number("mean_option_r", f.MeanOption).Flag("evaluable", f.Evaluable).EndObject();
        }

        evidence.EndArray().Array("epochs");
        evidence.Object().Text("epoch", "before " + input.LatestEpochFrom.ToString("yyyy-MM-dd", c)).Count("paired", earlier.Count).Number("mean_d", earlier.Count > 0 ? earlier.Average(p => p.D) : null).EndObject();
        evidence.Object().Text("epoch", "from " + input.LatestEpochFrom.ToString("yyyy-MM-dd", c) + " (nifty_expiry_tuesday)").Count("paired", latest.Count).Number("mean_d", latestMean).EndObject();
        evidence.EndArray().Array("lab_gates");
        foreach (var reason in gateReasons)
        {
            evidence.Item(reason);
        }

        evidence.EndArray();
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        var outcome = new ProtocolOutcome(verdict, missing, failed, limits, estimates.Finish(), evidence.Finish(), folds.Count, evaluable.Count, pool.Count);
        var lines = new List<string>
        {
            string.Create(c, $"ledger: N {input.Trials}, F {factor:F2}, margin {required:F3} R"),
            string.Create(c, $"paired {dev.Count} ({pool.Count} in {evaluable.Count} evaluable of {folds.Count} quarters); unpaired {unpairedTotal}: {string.Join(", ", input.Unpaired.OrderBy(r => r.Key, StringComparer.Ordinal).Where(r => r.Value > 0).Select(r => r.Key + " " + r.Value.ToString(c) + " (" + (total > 0 ? r.Value * 100.0 / total : 0).ToString("F1", c) + " %)"))}"),
            "mean d (option - cash, R): " + Fmt(meanD) + "; sd " + Fmt(sd) + "; design effect " + Fmt(designEffect) + "; minimum detectable effect " + Fmt(mde) + " R",
            string.Create(c, $"latest epoch from {input.LatestEpochFrom:yyyy-MM-dd}: n {latest.Count}, mean d {Fmt(latestMean)}"),
            "Black-76 fallback share " + Pct(fallbackShare) + "; derived expiry share " + Pct(derivedShare) + "; spread from the recent archive share " + Pct(recentSpreadShare),
            string.Create(c, $"sizable-only (lot feasible): n {sizable.Count}, mean d {Fmt(sizable.Count > 0 ? sizable.Average(p => p.D) : null)}, day-block lower {Fmt(sizableBounds?.Lower)}"),
            "futures baseline: NOT_EVALUABLE no_futures_bars",
        };
        return Result.Success(new ProtocolOResult(outcome, dropped, lines));
    }

    /// <summary>The one reading of the hold-out: <paramref name="pairs"/> are the paired trades dated on or after the hold-out start. KEEP (confirmed) when there are at least 100, they are net, the mean d is above 0 and its day-block lower bound is above 0; INCONCLUSIVE when not; NOT_EVALUABLE under 100 or without a bound.</summary>
    public static Result<ProtocolOutcome> HoldOut(IReadOnlyList<ExpressionPair> pairs, ExpressionStudyOptions options, DateOnly holdOutStart)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid || pairs.Any(p => !double.IsFinite(p.OptionR) || !double.IsFinite(p.CashR)))
        {
            return Result.Failure<ProtocolOutcome>(Error.Validation("extensions.options", "The hold-out needs valid options and a finite R_equiv for every paired trade."));
        }

        var hold = pairs.Where(p => p.Date >= holdOutStart).OrderBy(p => p.EntryEpochSeconds).ThenBy(p => p.TradeKey, StringComparer.Ordinal).ToList();
        var missing = new List<string>();
        if (hold.Count < options.MinHoldOutPaired)
        {
            missing.Add(string.Create(CultureInfo.InvariantCulture, $"hold-out paired trades {hold.Count} of {options.MinHoldOutPaired}"));
        }

        BootstrapBounds? bounds = null;
        if (missing.Count == 0)
        {
            bounds = BlockBootstrap.Bounds([.. DayBlocks(hold, p => p.D).Select(d => new BootstrapBlock(d.Sum, d.Count))], options.Draws, options.Seed, options.Level);
            if (bounds.Lower is null)
            {
                missing.Add("hold-out day-block bound unavailable");
            }
        }

        var failed = new List<string>();
        double? mean = hold.Count > 0 ? hold.Average(p => p.D) : null;
        if (missing.Count == 0 && hold.Any(p => !p.Net))
        {
            failed.Add("a hold-out trade is gross: a confirmation needs the owner's charge lines on both sides");
        }

        if (missing.Count == 0 && !(mean > 0 && bounds!.Lower > 0))
        {
            failed.Add("the hold-out mean d is not above 0 with a day-block lower bound above 0");
        }

        var verdict = missing.Count > 0 ? ExtensionVerdict.NotEvaluable : failed.Count == 0 ? ExtensionVerdict.Keep : ExtensionVerdict.Inconclusive;
        using var estimates = new ExtensionJson();
        estimates.Object().Count("hold_out_paired", hold.Count).Number("hold_out_mean_d", missing.Count == 0 ? mean : null).Number("day_lower", bounds?.Lower).Text("hold_out_start", holdOutStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).EndObject();
        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol).Text("read", "hold-out, once");
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        return Result.Success(new ProtocolOutcome(verdict, missing, failed, ["The hold-out is read once; this record is that reading.", "Advisory research only: a KEEP is a record, the expression stays cash."], estimates.Finish(), evidence.Finish(), 1, 0, hold.Count));
    }

    private static DateOnly QuarterStart(DateOnly date) => new(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1);

    private static string QuarterLabel(DateOnly start) => string.Create(CultureInfo.InvariantCulture, $"{start.Year}Q{((start.Month - 1) / 3) + 1}");

    private static List<Day> DayBlocks(IReadOnlyList<ExpressionPair> pairs, Func<ExpressionPair, double> value) =>
        [.. pairs.GroupBy(p => p.Date).OrderBy(g => g.Key).Select(g => new Day(g.Key, g.Sum(value), g.Count()))];

    private static List<Day> SessionBlocks(IReadOnlyList<Day> days, int size) =>
        [.. days.Select((d, i) => (d, i)).GroupBy(t => t.i / size).OrderBy(g => g.Key).Select(g => new Day(g.First().d.Date, g.Sum(t => t.d.Sum), g.Sum(t => t.d.Count)))];

    private static double Sd(List<double> values, double mean) => Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));

    private static double MaxDrawdown(IReadOnlyList<ExpressionPair> ordered)
    {
        double cumulative = 0, peak = 0, worst = 0;
        foreach (var p in ordered.OrderBy(p => p.ExitEpochSeconds).ThenBy(p => p.TradeKey, StringComparer.Ordinal))
        {
            cumulative += p.OptionR;
            peak = Math.Max(peak, cumulative);
            worst = Math.Max(worst, peak - cumulative);
        }

        return worst;
    }

    private static string Pct(double? v) => v is { } x ? (x * 100).ToString("F1", CultureInfo.InvariantCulture) + " %" : "unavailable";

    private static string Fmt(double? v) => v is { } x ? x.ToString("F4", CultureInfo.InvariantCulture) : "unavailable";
}
