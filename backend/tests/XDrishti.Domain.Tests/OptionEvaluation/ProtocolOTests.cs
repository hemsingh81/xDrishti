using System.Text.Json;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Evaluation;

namespace XDrishti.Domain.Tests.OptionEvaluation;

/// <summary>P14-09 protocol O at the Domain: the hand values, the margin widened by N, each verdict row with unavailable inputs, the epoch split, the fallback cap, the gross rule, the hold-out cut and the hold-out reading, determinism.</summary>
public sealed class ProtocolOTests
{
    private static ExpressionPair P(double option, double cash, int i) =>
        new(new DateOnly(2024, 4, 1).AddDays(i), 1_700_000_000 + i, 1_700_000_900 + i, "H" + i, option, cash, true, false, false, false, true);

    [Fact]
    public void The_hand_values_of_five_paired_trades_give_the_differences_and_the_mean()
    {
        double[] option = [1.2, -1.0, 0.4, 2.0, -0.6];
        double[] cash = [1.0, -1.0, 0.5, 1.5, -0.5];

        var d = option.Select((o, i) => P(o, cash[i], i).D).ToArray();

        d.Zip(new[] { 0.2, 0, -0.1, 0.5, -0.1 }, (a, b) => Math.Abs(a - b)).ShouldAllBe(x => x < 1e-12);
        d.Average().ShouldBe(0.10, 1e-12);
    }

    [Theory]
    [InlineData(1, 0.05)]
    [InlineData(4, 0.075)]
    [InlineData(16, 0.10)]
    public void The_margin_is_005_R_times_the_factor_of_the_ledger_count(int trials, double margin)
    {
        var result = ExprPairs.Run(ExprPairs.Make(1, ExprPairs.Even(7, 86), 0.4), trials);

        using var estimates = JsonDocument.Parse(result.Outcome.EstimatesJson);
        estimates.RootElement.GetProperty("required").GetDouble().ShouldBe(margin, 1e-9);
        (0.05 * MultipleTesting.Factor(trials)).ShouldBe(margin, 1e-12);
    }

    [Fact]
    public void Six_evaluable_folds_need_four_positive_so_three_positive_fails_the_fold_rule()
    {
        int[] counts = [70, 70, 70, 70, 70, 70];
        var pairs = ExprPairs.Make(2, counts, 0.4, shift: d => (((d.Year * 12) + d.Month - 1 - ((2024 * 12) + 3)) / 3) % 2 == 1 ? -1.2 : 0);

        var outcome = ExprPairs.Run(pairs).Outcome;

        outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep);
        outcome.Failed.ShouldContain(f => f.Contains("folds, 4 needed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_planted_net_effect_with_seven_quarters_and_600_pairs_is_a_keep_at_N_16_and_prints_the_evidence()
    {
        var result = ExprPairs.Run(ExprPairs.Make(3, ExprPairs.Even(7, 86), 0.4));

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.Keep, string.Join("; ", result.Outcome.Failed.Concat(result.Outcome.Missing)));
        result.Outcome.FoldsEvaluable.ShouldBe(7);
        using var evidence = JsonDocument.Parse(result.Outcome.EvidenceJson);
        evidence.RootElement.GetProperty("basis").GetString().ShouldBe("net");
        evidence.RootElement.GetProperty("future_baseline").GetString().ShouldNotBeNull().ShouldContain("no_futures_bars");
        result.Lines.ShouldContain(l => l.StartsWith("mean d (option - cash, R):", StringComparison.Ordinal));
        result.Lines.ShouldContain(l => l.StartsWith("sizable-only", StringComparison.Ordinal));
        result.Outcome.Limits.ShouldContain(l => l.Contains("no_futures_bars", StringComparison.Ordinal));
    }

    [Fact]
    public void Five_evaluable_quarters_is_NOT_EVALUABLE_naming_folds_5_of_6()
    {
        var result = ExprPairs.Run(ExprPairs.Make(4, [100, 100, 100, 100, 100, 10, 10], 0.4));

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Outcome.Missing.ShouldBe(["folds 5 of 6"]);
    }

    [Fact]
    public void Fewer_than_400_paired_trades_is_NOT_EVALUABLE_naming_the_trades()
    {
        var result = ExprPairs.Run(ExprPairs.Make(5, ExprPairs.Even(7, 50), 0.4));

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Outcome.Missing.ShouldBe(["paired trades 350 of 400"]);
    }

    [Fact]
    public void No_paired_trade_at_all_is_NOT_EVALUABLE_and_a_missing_spread_is_named_when_it_is_the_cause()
    {
        var result = ExprPairs.Run([], 1, unpaired: new Dictionary<string, int> { ["spread_unavailable"] = 50, ["no_series"] = 10 });

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Outcome.Missing.ShouldContain(m => m.StartsWith("no_paired_trades", StringComparison.Ordinal));
        result.Outcome.Missing.ShouldContain(m => m.StartsWith("spread_unavailable (50 of 60", StringComparison.Ordinal));
        result.Outcome.Missing.ShouldContain("folds 0 of 6");
        result.Outcome.Missing.ShouldContain("paired trades 0 of 400");
    }

    [Fact]
    public void When_every_paired_trade_lies_in_the_hold_out_the_shortfall_says_so_from_dates_and_not_no_paired_trades()
    {
        var input = ExprPairs.Input([], 1, null, null, new DateOnly(2025, 6, 20)) with { LastPaired = new DateOnly(2025, 12, 1) };

        var missing = ProtocolO.Evaluate(input).Value.Outcome.Missing;

        missing.ShouldContain("paired trades 0 of 400 (every paired trade lies in the hold-out from 2025-06-20)");
        missing.ShouldNotContain(m => m.StartsWith("no_paired_trades", StringComparison.Ordinal));
        ProtocolO.Evaluate(input with { LastPaired = null }).Value.Outcome.Missing.ShouldContain(m => m.StartsWith("no_paired_trades", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bound_that_is_unavailable_is_NOT_EVALUABLE_and_never_a_pass()
    {
        var pairs = ExprPairs.Make(6, [450], 0.4).Select(p => p with { Date = new DateOnly(2024, 5, 6) }).ToList();

        var result = ExprPairs.Run(pairs, 1, ExpressionStudyOptions.Default with { MinFolds = 1 });

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        result.Outcome.Missing.ShouldContain("day-block bound unavailable");
    }

    [Fact]
    public void The_evidence_of_a_non_value_is_the_text_unavailable_and_never_a_digit()
    {
        var result = ExprPairs.Run([], 1);

        using var estimates = JsonDocument.Parse(result.Outcome.EstimatesJson);
        foreach (var name in new[] { "mean_d", "sd_d", "design_effect", "mde", "day_lower", "day_upper", "session_lower", "required", "latest_epoch_mean_d", "fallback_share", "sizable_day_lower" })
        {
            estimates.RootElement.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.String, name);
            estimates.RootElement.GetProperty(name).GetString().ShouldBe("unavailable", name);
        }

        using var evidence = JsonDocument.Parse(result.Outcome.EvidenceJson);
        evidence.RootElement.GetProperty("basis").GetString().ShouldBe("unavailable");
        result.Lines.ShouldContain(l => l.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gross_result_is_inconclusive_whichever_way_it_points_and_says_when_its_upper_bound_is_below_the_margin()
    {
        var up = ExprPairs.Run(ExprPairs.Make(7, ExprPairs.Even(7, 86), 0.4, net: false)).Outcome;
        var down = ExprPairs.Run(ExprPairs.Make(8, ExprPairs.Even(7, 86), -0.5, net: false)).Outcome;
        var netDown = ExprPairs.Run(ExprPairs.Make(8, ExprPairs.Even(7, 86), -0.5)).Outcome;

        up.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        up.Failed.ShouldContain(f => f.StartsWith("gross:", StringComparison.Ordinal));
        down.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        down.Limits.ShouldContain("gross upper bound below margin");
        netDown.Verdict.ShouldBe(ExtensionVerdict.Drop);
        netDown.Limits.ShouldNotContain("gross upper bound below margin");
    }

    [Fact]
    public void A_net_effect_between_zero_and_the_margin_is_inconclusive_and_not_a_drop()
    {
        var outcome = ExprPairs.Run(ExprPairs.Make(9, ExprPairs.Even(7, 86), 0.02)).Outcome;

        outcome.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
    }

    [Fact]
    public void A_fallback_share_of_6_percent_is_inconclusive_with_the_reason_and_5_percent_can_be_a_keep()
    {
        var six = ExprPairs.Run(ExprPairs.Make(10, ExprPairs.Even(7, 86), 0.4, fallback: 36)).Outcome;
        var five = ExprPairs.Run(ExprPairs.Make(10, ExprPairs.Even(7, 86), 0.4, fallback: 30)).Outcome;

        six.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        six.Failed.ShouldContain(f => f.Contains("Black-76 fallback share 6.0 % above 5 %", StringComparison.Ordinal));
        five.Verdict.ShouldBe(ExtensionVerdict.Keep, string.Join("; ", five.Failed));
    }

    [Fact]
    public void A_gain_that_turned_negative_in_the_latest_epoch_is_not_a_keep()
    {
        var pairs = ExprPairs.Make(11, ExprPairs.Even(7, 86), 0.4, shift: d => d >= ExprPairs.EpochFrom ? -1.0 : 0);

        var outcome = ExprPairs.Run(pairs).Outcome;

        outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep);
        outcome.Failed.ShouldContain(f => f.Contains("in the latest epoch", StringComparison.Ordinal) && f.Contains("not above 0", StringComparison.Ordinal));
    }

    [Fact]
    public void With_12_pairs_in_the_latest_epoch_a_keep_is_not_met_and_the_count_is_named()
    {
        var pairs = ExprPairs.Make(12, ExprPairs.Even(7, 86), 0.4);
        var epoch = ExprPairs.Start.AddMonths(18).AddDays(68); // the last 12 days of the last quarter's 80 distinct dates

        var outcome = ExprPairs.Run(pairs, epochFrom: epoch).Outcome;

        outcome.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        outcome.Failed.ShouldContain("latest epoch n 12 of 30");
        using var estimates = JsonDocument.Parse(outcome.EstimatesJson);
        estimates.RootElement.GetProperty("latest_epoch_mean_d").GetString().ShouldBe("unavailable");
    }

    [Fact]
    public void An_unpaired_share_above_30_percent_is_printed_as_a_limit()
    {
        var result = ExprPairs.Run(ExprPairs.Make(13, ExprPairs.Even(7, 86), 0.4), unpaired: new Dictionary<string, int> { ["no_series"] = 400 });

        result.Outcome.Limits.ShouldContain(l => l.StartsWith("unpaired share", StringComparison.Ordinal) && l.Contains("above 30 %", StringComparison.Ordinal));
        result.Lines.ShouldContain(l => l.Contains("no_series 400", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_pairs_in_any_order_give_the_same_record_text()
    {
        var pairs = ExprPairs.Make(14, ExprPairs.Even(7, 86), 0.4);
        var shuffled = pairs.OrderBy(p => p.TradeKey.GetHashCode(StringComparison.Ordinal) * 31 + p.Date.DayNumber).Reverse().ToList();

        var a = ExprPairs.Run(pairs).Outcome;
        var b = ExprPairs.Run(shuffled).Outcome;

        b.EstimatesJson.ShouldBe(a.EstimatesJson);
        b.EvidenceJson.ShouldBe(a.EvidenceJson);
        b.Verdict.ShouldBe(a.Verdict);
    }

    [Fact]
    public void The_cut_drops_the_embargo_dates_and_pairs_that_leave_on_or_after_the_start_and_keeps_the_hold_out_out()
    {
        var pairs = ExprPairs.Make(15, ExprPairs.Even(7, 86), 0.4);
        var last = pairs.Max(p => p.Date);
        var start = ProtocolO.HoldOutStartOf(last, ExpressionStudyOptions.Default);

        var (dev, dropped) = ProtocolO.Cut(pairs, ExpressionStudyOptions.Default, start);

        start.ShouldBe(last.AddMonths(-6).AddDays(1));
        dev.ShouldAllBe(p => p.Date < start);
        dropped.ShouldBeGreaterThan(0);
        dev.Select(p => p.Date).Distinct().Order().Last().ShouldBeLessThan(pairs.Where(p => p.Date < start).Max(p => p.Date));
    }

    [Fact]
    public void The_hold_out_is_confirmed_only_with_100_net_pairs_a_positive_mean_and_a_lower_bound_above_zero()
    {
        var start = new DateOnly(2025, 6, 1);
        var good = ExprPairs.Make(16, [150], 0.4).Select(p => p with { Date = start.AddDays((int)(p.EntryEpochSeconds % 40)) }).ToList();
        var few = good.Take(99).ToList();
        var flat = ExprPairs.Make(17, [150], 0.0).Select(p => p with { Date = start.AddDays((int)(p.EntryEpochSeconds % 40)) }).ToList();
        var gross = good.Select(p => p with { Net = false }).ToList();

        ProtocolO.HoldOut(good, ExpressionStudyOptions.Default, start).Value.Verdict.ShouldBe(ExtensionVerdict.Keep);
        var thin = ProtocolO.HoldOut(few, ExpressionStudyOptions.Default, start).Value;
        thin.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        thin.Missing.ShouldBe(["hold-out paired trades 99 of 100"]);
        ProtocolO.HoldOut(flat, ExpressionStudyOptions.Default, start).Value.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ProtocolO.HoldOut(gross, ExpressionStudyOptions.Default, start).Value.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        using var estimates = JsonDocument.Parse(thin.EstimatesJson);
        estimates.RootElement.GetProperty("hold_out_mean_d").GetString().ShouldBe("unavailable");
    }

    [Fact]
    public void A_non_finite_R_or_a_ledger_count_under_one_is_refused()
    {
        var pairs = ExprPairs.Make(18, [10], 0.4);

        ProtocolO.Evaluate(ExprPairs.Input([.. pairs, pairs[0] with { OptionR = double.NaN }])).IsFailure.ShouldBeTrue();
        ProtocolO.Evaluate(ExprPairs.Input(pairs, 0)).IsFailure.ShouldBeTrue();
        ProtocolO.Evaluate(ExprPairs.Input(pairs, 1, ExpressionStudyOptions.Default with { Draws = 1 })).IsFailure.ShouldBeTrue();
    }
}
