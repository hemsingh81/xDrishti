using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.PairResearch;

/// <summary>
/// Protocol S of P14-13 (same-sector cash pairs, family <c>pairs:intraday-1</c>): the verdict over the stored trades of the pre-registered primary variant (e 2.0, L 5). The folds are calendar quarters (a quarter with at least 20 pair trades is evaluable); the bounds are one-sided 90 % block-bootstrap
/// bounds of the mean R by day blocks and, as the stated sensitivity, by 5-session blocks (10,000 draws, seed 1337). NOT_EVALUABLE names every missing quantity (quarters, pooled trades, a bound). KEEP needs all of: both lower bounds at or above <c>0.05 R x F</c> with <c>F = Factor(N)</c>; a positive mean R in two thirds of
/// the evaluable quarters; the Lab's own gates through <see cref="VerdictEvaluator"/> with N; no pair at 25 % or more of the net profit; net of the owner's costs. DROP needs the minima, not KEEP, and the larger upper bound below <c>0.05 R</c> (not widened): a gross DROP is valid, a gross KEEP is not
/// (it is INCONCLUSIVE). The last six months are the hold-out: <see cref="Evaluate"/> never reads them; <see cref="HoldOut"/> is the one reading. An unavailable input never passes a rule. Pure and seeded.
/// </summary>
public static class ProtocolS
{
    /// <summary>The protocol letter.</summary>
    public const string Protocol = PairFamilies.Protocol;

    private sealed record Day(DateOnly Date, double Sum, int Count);

    /// <summary>The development verdict.</summary>
    public static Result<ProtocolSResult> Evaluate(ProtocolSInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var o = input.Options;
        if (input.Trials < 1 || !o.IsValid || input.Trades.Any(t => !double.IsFinite(t.R)))
        {
            return Result.Failure<ProtocolSResult>(Error.Validation("extensions.options", "Protocol S needs the ledger count (at least 1), valid options and a finite R for every trade."));
        }

        var c = CultureInfo.InvariantCulture;
        var (dev, holdStart, dropped) = Cut(input.Trades, o, input.HoldOutStart);
        var gross = dev.Any(t => t.NetInr is null);
        var factor = MultipleTesting.Factor(input.Trials);
        var required = o.MarginR * factor;
        var quarters = dev.GroupBy(t => PairQuarter.StartOf(t.Date)).OrderBy(g => g.Key)
            .Select(g => (Start: g.Key, N: g.Count(), Mean: g.Average(t => t.R), Evaluable: g.Count() >= o.MinTradesPerQuarter)).ToList();
        var evaluable = quarters.Where(q => q.Evaluable).ToList();
        var pool = dev.Where(t => evaluable.Any(q => q.Start == PairQuarter.StartOf(t.Date))).ToList();

        var missing = new List<string>();
        if (evaluable.Count < o.MinQuarters)
        {
            missing.Add(string.Create(c, $"quarters {evaluable.Count} of {o.MinQuarters}"));
        }

        if (pool.Count < o.MinTrades)
        {
            missing.Add(string.Create(c, $"pair trades {pool.Count} of {o.MinTrades}"));
        }

        BootstrapBounds? dayBounds = null;
        BootstrapBounds? sessionBounds = null;
        if (missing.Count == 0)
        {
            var days = DayBlocks(pool);
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

        var failed = new List<string>();
        var positive = evaluable.Count(q => q.Mean > 0);
        var needed = (int)Math.Ceiling((evaluable.Count * o.PositiveQuarterShare) - 1e-9);
        double? topShare = null;
        string? topPair = null;
        var gateReasons = new List<string>();
        double? mean = pool.Count > 0 ? pool.Average(t => t.R) : null;
        double? drawdown = null;
        var unrepairable = false;
        if (missing.Count == 0)
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
                failed.Add(string.Create(c, $"mean R above zero in {positive} of {evaluable.Count} quarters, {needed} needed"));
            }

            drawdown = MaxDrawdown(pool);
            var wins = pool.Where(t => t.R > 0).Sum(t => t.R);
            var losses = -pool.Where(t => t.R < 0).Sum(t => t.R);
            var profitFactor = losses > 0 ? wins / losses : wins > 0 ? double.PositiveInfinity : 0;
            var lab = VerdictEvaluator.Evaluate(o.Gates, new OosEvidence(pool.Count, mean!.Value, profitFactor, evaluable.Count, positive, drawdown.Value, 0, OosEvidence.WalkForward) /* calibration error: not applicable, 0 cannot fail the gate */, input.Trials, o.MinQuarters);
            gateReasons.AddRange(lab.Gates.Select(g => g.Reason));
            if (lab.Outcome != VerdictOutcome.Pass)
            {
                failed.Add("the Lab's gates are not all met: " + string.Join("; ", lab.Gates.Where(g => !g.Passed).Select(g => g.Name).Concat(lab.Limits)));
            }

            var byPair = pool.GroupBy(t => t.PairKey, StringComparer.Ordinal).Select(g => (Pair: g.Key, Profit: g.Sum(t => t.NetInr ?? t.GrossInr))).OrderByDescending(p => p.Profit).ThenBy(p => p.Pair, StringComparer.Ordinal).ToList();
            var total = byPair.Sum(p => p.Profit);
            if (!(total > 0))
            {
                failed.Add("no net profit; concentration not computable");
            }
            else
            {
                topPair = byPair[0].Pair;
                topShare = (double)(byPair[0].Profit / total);
                if (topShare >= o.MaxPairShare)
                {
                    failed.Add(string.Create(c, $"pair {topPair} makes {topShare * 100:F1} % of the net profit, {o.MaxPairShare * 100:F0} % allowed"));
                }
            }

            if (gross)
            {
                failed.Add("gross: the owner's charge sheet was not applied, so a KEEP is not allowed (a gross DROP is)");
            }

            unrepairable = Math.Max(dayBounds.Upper!.Value, sessionBounds.Upper!.Value) < o.MarginR;
        }

        var verdict = VerdictTable.Decide(missing, failed, unrepairable);
        string[] limits =
        [
            "ban_list: unavailable (no F&O ban source exists; never read as 'not banned')",
            "sector: as of today (a survivorship-like limit); asm_gsm: as of today",
            "p-values: asymptotic (MacKinnon 1994)",
            gross ? "costs: gross, no owner charge sheet (ISS-25); a gross KEEP is impossible, a gross DROP is valid" : "costs: net of the owner's charge sheet, four intraday cash fills per trade",
            "Advisory research only: nothing selects, plans or orders a pair.",
        ];

        using var estimates = new ExtensionJson();
        estimates.Object().Count("trades", dev.Count).Count("pooled_trades", pool.Count).Count("quarters", quarters.Count).Count("evaluable_quarters", evaluable.Count).Count("positive_quarters", positive).Number("mean_r", mean)
            .Number("day_lower", dayBounds?.Lower).Number("day_upper", dayBounds?.Upper).Number("session_lower", sessionBounds?.Lower).Number("session_upper", sessionBounds?.Upper).Number("required", missing.Count == 0 ? required : null)
            .Number("top_pair_share", topShare).Text("top_pair", topPair).Number("max_drawdown_r", drawdown).Count("embargo_dropped", dropped).EndObject();

        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol).Count("ledger_trials", input.Trials).Number("factor", factor).Text("variant", PairVariant.Primary.Label).Text("basis", gross ? "gross" : "net").Text("calibration_gate", "not applicable (no probability)").Text("hold_out_start", holdStart?.ToString("yyyy-MM-dd", c)).Array("quarters");
        foreach (var q in quarters)
        {
            evidence.Object().Text("quarter", PairQuarter.Label(q.Start)).Count("trades", q.N).Number("mean_r", q.Mean).Flag("evaluable", q.Evaluable).EndObject();
        }

        evidence.EndArray().Array("variant_grid");
        foreach (var g in input.Grid.OrderBy(g => g.Variant, StringComparer.Ordinal))
        {
            evidence.Object().Text("variant", g.Variant).Count("trades", g.Trades).Number("mean_r", g.MeanR).EndObject();
        }

        evidence.EndArray().Object("formations");
        WriteFormations(evidence, input.Formations);
        var turnover = dev.Count > 0 ? (double)(dev.Sum(t => t.TurnoverInr) / dev.Count) : (double?)null;
        evidence.EndObject().Number("turnover_per_trade_inr", turnover).Array("lab_gates");
        foreach (var reason in gateReasons)
        {
            evidence.Item(reason);
        }

        evidence.EndArray();
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        var outcome = new ProtocolOutcome(verdict, missing, failed, limits, estimates.Finish(), evidence.Finish(), quarters.Count, evaluable.Count, pool.Count);
        return Result.Success(new ProtocolSResult(outcome, holdStart, dropped));
    }

    /// <summary>The one reading of the hold-out (the last six months): KEEP when there are at least 100 hold-out trades, the mean R is above 0 and its day-block lower bound is above 0; INCONCLUSIVE when not; NOT_EVALUABLE under 100 trades or without a bound.</summary>
    public static Result<ProtocolOutcome> HoldOut(IReadOnlyList<PairTradeSample> trades, PairStudyOptions options)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid || trades.Any(t => !double.IsFinite(t.R)))
        {
            return Result.Failure<ProtocolOutcome>(Error.Validation("extensions.options", "The hold-out needs valid options and a finite R for every trade."));
        }

        var (_, holdStart, _) = Cut(trades, options);
        var hold = holdStart is { } start ? trades.Where(t => t.Date >= start).OrderBy(t => t.EntryEpochSeconds).ThenBy(t => t.PairKey, StringComparer.Ordinal).ThenBy(t => t.R).ToList() : [];
        var missing = new List<string>();
        if (hold.Count < options.MinHoldOutTrades)
        {
            missing.Add(string.Create(CultureInfo.InvariantCulture, $"hold-out pair trades {hold.Count} of {options.MinHoldOutTrades}"));
        }

        BootstrapBounds? bounds = null;
        if (missing.Count == 0)
        {
            bounds = BlockBootstrap.Bounds([.. DayBlocks(hold).Select(d => new BootstrapBlock(d.Sum, d.Count))], options.Draws, options.Seed, options.Level);
            if (bounds.Lower is null)
            {
                missing.Add("hold-out day-block bound unavailable");
            }
        }

        var failed = new List<string>();
        double? mean = hold.Count > 0 ? hold.Average(t => t.R) : null;
        if (missing.Count == 0 && hold.Any(t => t.NetInr is null))
        {
            failed.Add("a hold-out trade is gross or its costs are unavailable: a hold-out confirmation needs net results");
        }

        if (missing.Count == 0 && !(mean > 0 && bounds!.Lower > 0))
        {
            failed.Add("the hold-out mean R is not above 0 with a day-block lower bound above 0");
        }

        var verdict = missing.Count > 0 ? ExtensionVerdict.NotEvaluable : failed.Count == 0 ? ExtensionVerdict.Keep : ExtensionVerdict.Inconclusive;
        using var estimates = new ExtensionJson();
        estimates.Object().Count("hold_out_trades", hold.Count).Number("hold_out_mean_r", missing.Count == 0 ? mean : null).Number("day_lower", bounds?.Lower).Text("hold_out_start", holdStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).EndObject();
        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol).Text("read", "hold-out, once");
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        return Result.Success(new ProtocolOutcome(verdict, missing, failed, ["The hold-out is read once; this record is that reading.", "Advisory research only: a KEEP is a record."], estimates.Finish(), evidence.Finish(), 1, 0, hold.Count));
    }

    /// <summary>Splits the trades into development and hold-out by date. The hold-out starts six months before the last trade date (plus a day); the development trades of the five trading dates before it are dropped (the embargo, counted).</summary>
    public static (IReadOnlyList<PairTradeSample> Development, DateOnly? HoldOutStart, int EmbargoDropped) Cut(IReadOnlyList<PairTradeSample> trades, PairStudyOptions options, DateOnly? holdOutStart = null)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);
        if (trades.Count == 0)
        {
            return ([], null, 0);
        }

        var start = holdOutStart ?? trades.Max(t => t.Date).AddMonths(-options.HoldOutMonths).AddDays(1);

        // A canonical order, so that sums (and so the stored numbers) do not depend on the order the rows were read in.
        var before = trades.Where(t => t.Date < start).OrderBy(t => t.EntryEpochSeconds).ThenBy(t => t.PairKey, StringComparer.Ordinal).ThenBy(t => t.R).ToList();
        var embargoed = before.Select(t => t.Date).Distinct().OrderDescending().Take(options.EmbargoSessions).ToHashSet();
        var development = embargoed.Count == 0 ? before : [.. before.Where(t => !embargoed.Contains(t.Date))];
        return (development, start, before.Count - development.Count);
    }

    private static List<Day> DayBlocks(IReadOnlyList<PairTradeSample> trades) =>
        [.. trades.GroupBy(t => t.Date).OrderBy(g => g.Key).Select(g => new Day(g.Key, g.Sum(t => t.R), g.Count()))];

    private static List<Day> SessionBlocks(IReadOnlyList<Day> days, int size) =>
        [.. days.Select((d, i) => (d, i)).GroupBy(t => t.i / size).OrderBy(g => g.Key).Select(g => new Day(g.First().d.Date, g.Sum(t => t.d.Sum), g.Sum(t => t.d.Count)))];

    private static double MaxDrawdown(IReadOnlyList<PairTradeSample> ordered)
    {
        double cumulative = 0, peak = 0, worst = 0;
        foreach (var t in ordered)
        {
            cumulative += t.R;
            peak = Math.Max(peak, cumulative);
            worst = Math.Max(worst, peak - cumulative);
        }

        return worst;
    }

    private static void WriteFormations(ExtensionJson json, IReadOnlyList<PairFormationStat> formations)
    {
        var all = formations.SelectMany(f => f.HalfLives).Order().ToArray();
        json.Count("count", formations.Count).Number("share_without_selected_pair", formations.Count > 0 ? formations.Count(f => f.Selected == 0) / (double)formations.Count : null)
            .Number("half_life_min", all.Length > 0 ? all[0] : null).Number("half_life_median", all.Length > 0 ? all[all.Length / 2] : null).Number("half_life_max", all.Length > 0 ? all[^1] : null).Array("by_quarter");
        foreach (var f in formations.OrderBy(f => f.Quarter, StringComparer.Ordinal))
        {
            json.Object().Text("quarter", f.Quarter).Count("candidates", f.Candidates).Count("tested", f.Tested).Count("unavailable", f.Unavailable).Count("selected", f.Selected).EndObject();
        }

        json.EndArray();
    }

    private static string Fmt(double? v) => v is { } x ? x.ToString("F4", CultureInfo.InvariantCulture) : "unavailable";
}
