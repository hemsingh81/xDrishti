using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The experiment judge (spec P6-11, ADR 0072): hand tables for the folds, the required improvement, the total-R rule, and the honesty rules (a cheaper slippage cannot win, fewer than 6 folds cannot say better).</summary>
public sealed class ExperimentJudgeTests
{
    private static readonly ExperimentKnob Stop = new("intraday_stop_atr", ExperimentKnobKind.Parameter, "1.2", "1.0");

    private static readonly ExperimentKnob Slip = new("slippage_bps", ExperimentKnobKind.Slippage, "8", "4");

    /// <summary>Trades on days 1 to <paramref name="perMonth"/> of every month from <paramref name="start"/> for <paramref name="months"/> months. R by the two functions of the month index and the day.</summary>
    private static List<ExperimentTrade> Series(DateOnly start, int months, int perMonth, Func<int, int, double> baseline, Func<int, int, double?> candidate)
    {
        var trades = new List<ExperimentTrade>();
        for (var m = 0; m < months; m++)
        {
            var first = start.AddMonths(m);
            for (var d = 0; d < perMonth; d++)
            {
                var date = new DateOnly(first.Year, first.Month, d + 1);
                trades.Add(new ExperimentTrade($"S|N{m}_{d}|Buy|{date:yyyy-MM-dd}", date, baseline(m, d), candidate(m, d)));
            }
        }

        return trades;
    }

    /// <summary>36 months from 2022-01: nine quarterly folds, 15 trades a month.</summary>
    private static List<ExperimentTrade> NineFolds(double baseR, double? candidateR) =>
        Series(new DateOnly(2022, 1, 1), 36, 15, (_, _) => baseR, (_, _) => candidateR);

    [Fact]
    public void A_single_fold_is_computed_by_hand_and_is_inconclusive()
    {
        // 5 early trades at R 0 set the first date (2024-01); the only fold is 2024-10 to 2024-12 with 10 trades a side.
        var trades = Series(new DateOnly(2024, 1, 1), 3, 2, (_, _) => 0, (_, _) => 0).Take(5).ToList();
        trades.AddRange(Series(new DateOnly(2024, 10, 1), 3, 4, (_, _) => 1.0, (_, _) => 1.5).Take(10).Select(t => t with { Key = "Q4|" + t.Key }));

        var report = ExperimentJudge.Judge(Stop, trades, new ExperimentOptions()).Value;

        report.Folds.Count.ShouldBe(1);
        var fold = report.Folds[0];
        fold.Usable.ShouldBeTrue();
        fold.BaselineMeanR.ShouldBe(1.0);
        fold.CandidateMeanR.ShouldBe(1.5);
        fold.Difference.ShouldBe(0.5);
        fold.Better.ShouldBeTrue();
        report.RequiredImprovementR.ShouldBe(0.026, 1e-12);
        report.FoldsNeeded.ShouldBe(1);
        report.FoldsBetter.ShouldBe(1);
        report.BaselineTotalR.ShouldBe((5 * -0.02) + (10 * 0.98), 1e-9);
        report.CandidateTotalR.ShouldBe((5 * -0.02) + (10 * 1.48), 1e-9);
        report.Outcome.ShouldBe(ExperimentOutcome.Inconclusive);
        report.Reasons.ShouldContain(r => r.Contains("only 1 used folds, 6 needed"));
    }

    [Fact]
    public void A_clear_gain_in_nine_folds_is_better()
    {
        var report = ExperimentJudge.Judge(Stop, NineFolds(0.10, 0.30), new ExperimentOptions()).Value;

        report.UsedFolds.ShouldBe(9);
        report.FoldsBetter.ShouldBe(9);
        report.FoldsNeeded.ShouldBe(6);
        report.Outcome.ShouldBe(ExperimentOutcome.Better);
        report.Reasons.ShouldBeEmpty();
        report.PooledCandidateMeanR!.Value.ShouldBe(0.30, 1e-9);
    }

    [Fact]
    public void Fewer_than_six_folds_cannot_say_better_however_good_the_numbers()
    {
        // 2023-01 to 2024-12 gives five folds (2023-10 to 2024-12).
        var trades = Series(new DateOnly(2023, 1, 1), 24, 15, (_, _) => 0.1, (_, _) => 0.9);

        var report = ExperimentJudge.Judge(Stop, trades, new ExperimentOptions()).Value;

        report.UsedFolds.ShouldBe(5);
        report.FoldsBetter.ShouldBe(5);
        report.Outcome.ShouldBe(ExperimentOutcome.Inconclusive);
        report.Limits.ShouldContain(l => l.Contains("Fewer than 6 folds"));
    }

    [Fact]
    public void A_command_line_cannot_lower_the_floor_of_six_folds()
    {
        var trades = Series(new DateOnly(2023, 1, 1), 24, 15, (_, _) => 0.1, (_, _) => 0.9);

        var report = ExperimentJudge.Judge(Stop, trades, new ExperimentOptions(MinFolds: 2)).Value;

        report.Outcome.ShouldBe(ExperimentOutcome.Inconclusive);
        report.Options.MinFolds.ShouldBe(6);
        report.Limits.ShouldContain(l => l.Contains("raised to the floor of 6"));
    }

    [Fact]
    public void A_cheaper_slippage_cannot_win_whatever_the_numbers()
    {
        var report = ExperimentJudge.Judge(Slip, NineFolds(0.10, 0.50), new ExperimentOptions(), FillCostChange.Cheaper).Value;

        report.FoldsBetter.ShouldBe(9);
        report.Outcome.ShouldBe(ExperimentOutcome.CheaperAssumption);
        report.Reasons.ShouldContain(r => r.Contains("cheaper assumption cannot win"));
    }

    [Fact]
    public void A_dearer_slippage_is_judged_on_the_numbers_like_any_knob()
    {
        var dearer = new ExperimentKnob("slippage_bps", ExperimentKnobKind.Slippage, "8", "12");

        var report = ExperimentJudge.Judge(dearer, NineFolds(0.10, 0.50), new ExperimentOptions(), FillCostChange.Dearer).Value;

        report.Outcome.ShouldBe(ExperimentOutcome.Better);
    }

    [Theory]
    [InlineData(1, 0.026, true)]
    [InlineData(4, 0.032, true)]
    [InlineData(9, 0.038, false)]
    [InlineData(100, 0.080, false)]
    public void The_required_improvement_widens_with_the_trials(int trials, double required, bool betterByThirtyFiveThousandths)
    {
        // The candidate is 0.035 R per trade better in every fold: it clears 0.026 and 0.032 but not 0.038 or 0.080.
        var report = ExperimentJudge.Judge(Stop, NineFolds(0.10, 0.135), new ExperimentOptions(Trials: trials)).Value;

        report.RequiredImprovementR.ShouldBe(required, 1e-12);
        (report.Outcome == ExperimentOutcome.Better).ShouldBe(betterByThirtyFiveThousandths);
        if (!betterByThirtyFiveThousandths)
        {
            report.Outcome.ShouldBe(ExperimentOutcome.NotBetter);
            report.Reasons.ShouldContain(r => r.Contains("better in 0 of 9 used folds, 6 needed"));
        }
    }

    [Fact]
    public void Higher_expectancy_on_far_fewer_trades_is_not_better_when_total_r_falls_more_than_a_tenth()
    {
        // Baseline: 540 trades at R 1.0. Candidate: four in fifteen, at R 1.5: every fold is better, the total falls from 529.2 to 213.1.
        var trades = Series(new DateOnly(2022, 1, 1), 36, 15, (_, _) => 1.0, (_, d) => d is 0 or 3 or 7 or 11 ? 1.5 : null).ToList();

        var report = ExperimentJudge.Judge(Stop, trades, new ExperimentOptions()).Value;

        report.FoldsBetter.ShouldBe(9);
        report.BaselineTotalR.ShouldBe(540 * 0.98, 1e-9);
        report.CandidateTotalR.ShouldBe(144 * 1.48, 1e-9);
        report.Outcome.ShouldBe(ExperimentOutcome.NotBetter);
        report.Reasons.ShouldContain(r => r.Contains("total R after the live gap falls"));
    }

    [Fact]
    public void A_total_within_the_allowed_drop_passes()
    {
        // The candidate keeps 12 of 15 entries at a higher R: total 432 x 0.98... stays above 90 % of the baseline's.
        var trades = Series(new DateOnly(2022, 1, 1), 36, 15, (_, _) => 1.0, (_, d) => d < 12 ? 1.2 : null).ToList();

        var report = ExperimentJudge.Judge(Stop, trades, new ExperimentOptions()).Value;

        report.CandidateTotalR.ShouldBeGreaterThan(report.BaselineTotalR * 0.9);
        report.Outcome.ShouldBe(ExperimentOutcome.Better);
    }

    [Fact]
    public void A_fold_with_too_few_trades_on_a_side_is_not_used()
    {
        var trades = Series(new DateOnly(2022, 1, 1), 36, 15, (_, _) => 0.1, (_, d) => d < 2 ? 0.5 : null).ToList();

        var report = ExperimentJudge.Judge(Stop, trades, new ExperimentOptions()).Value;

        // The candidate took 2 entries a month, 6 a quarter: under the 10 needed on each side, so no fold is used.
        report.UsedFolds.ShouldBe(0);
        report.Folds.ShouldAllBe(f => !f.Usable && f.CandidateTrades == 6);
        report.Outcome.ShouldBe(ExperimentOutcome.Inconclusive);
    }

    [Fact]
    public void No_usable_fold_is_inconclusive_with_the_reason()
    {
        var trades = Series(new DateOnly(2024, 1, 1), 4, 5, (_, _) => 0.1, (_, _) => 0.9);

        var report = ExperimentJudge.Judge(Stop, trades, new ExperimentOptions()).Value;

        report.UsedFolds.ShouldBe(0);
        report.Outcome.ShouldBe(ExperimentOutcome.Inconclusive);
        report.Reasons.ShouldContain(r => r.Contains("no fold is usable"));
    }

    [Fact]
    public void The_same_trades_in_any_order_give_the_same_text()
    {
        var trades = NineFolds(0.10, 0.30);
        var shuffled = trades.OrderBy(t => t.Key.GetHashCode(StringComparison.Ordinal) % 97).ThenBy(t => t.Key, StringComparer.Ordinal).Reverse().ToList();

        var a = ExperimentJudge.Judge(Stop, trades, new ExperimentOptions()).Value.ToText();
        var b = ExperimentJudge.Judge(Stop, shuffled, new ExperimentOptions()).Value.ToText();

        b.ShouldBe(a);
        a.ShouldContain("experiment intraday_stop_atr (Parameter): 1.2 -> 1.0: Better");
        a.ShouldContain("limit: Replayed history with modelled fills");
    }

    [Fact]
    public void The_hold_out_is_refused_in_development()
    {
        var result = ExperimentJudge.Judge(Stop, NineFolds(0.1, 0.3), new ExperimentOptions(HoldOutStart: new DateOnly(2024, 1, 1)));

        result.Error.Code.ShouldBe("experiment.holdout_touched");
    }

    [Fact]
    public void Bad_input_is_a_failure_with_a_code()
    {
        var one = new ExperimentTrade("k", new DateOnly(2024, 1, 2), 0.1, 0.2);

        ExperimentJudge.Judge(Stop, [], new ExperimentOptions()).Error.Code.ShouldBe("experiment.no_trades");
        ExperimentJudge.Judge(Stop, [one, one], new ExperimentOptions()).Error.Code.ShouldBe("experiment.duplicate_key");
        ExperimentJudge.Judge(Stop, [one with { BaselineR = double.NaN }], new ExperimentOptions()).Error.Code.ShouldBe("experiment.bad_trade");
        ExperimentJudge.Judge(Stop, [one], new ExperimentOptions(Trials: 0)).Error.Code.ShouldBe("experiment.options");
        ExperimentJudge.Judge(Stop with { CandidateValue = Stop.BaselineValue }, [one], new ExperimentOptions()).Error.Code.ShouldBe("experiment.options");
        ExperimentJudge.Judge(Slip, [one], new ExperimentOptions()).Error.Code.ShouldBe("experiment.options");
    }

    [Fact]
    public void A_slippage_labelled_dearer_that_is_really_cheaper_cannot_win()
    {
        var mislabelled = new ExperimentKnob("slippage_bps", ExperimentKnobKind.Slippage, "4", "8");

        var report = ExperimentJudge.Judge(mislabelled, NineFolds(0.10, 0.50), new ExperimentOptions(), FillCostChange.Cheaper).Value;

        report.Outcome.ShouldBe(ExperimentOutcome.CheaperAssumption);
    }

    [Fact]
    public void A_slippage_labelled_cheaper_that_is_really_dearer_is_judged_on_the_numbers()
    {
        var report = ExperimentJudge.Judge(Slip, NineFolds(0.10, 0.50), new ExperimentOptions(), FillCostChange.Dearer).Value;

        report.Outcome.ShouldBe(ExperimentOutcome.Better);
    }

    [Fact]
    public void Slippage_components_that_cannot_be_ordered_count_as_cheaper()
    {
        var report = ExperimentJudge.Judge(Slip, NineFolds(0.10, 0.50), new ExperimentOptions(), FillCostChange.Mixed).Value;

        report.Outcome.ShouldBe(ExperimentOutcome.CheaperAssumption);
        report.Reasons.ShouldContain(r => r.Contains("cannot be ordered"));
    }

    [Fact]
    public void The_fold_trade_minimum_and_the_live_gap_cannot_be_lowered_below_their_floors()
    {
        var report = ExperimentJudge.Judge(Stop, NineFolds(0.1, 0.3), new ExperimentOptions(MinFoldTrades: 3, ExpectedLiveGapR: 0)).Value;

        report.Options.MinFoldTrades.ShouldBe(10);
        report.Options.ExpectedLiveGapR.ShouldBe(0.02);
        report.Limits.ShouldContain(l => l.Contains("raised to the floor of 10"));
        report.Limits.ShouldContain(l => l.Contains("expected live gap of 0.000 R was raised"));
        report.Limits.ShouldContain(l => l.Contains("not recorded and does not count as a trial"));
    }

    [Fact]
    public void An_experiment_without_a_hold_out_is_never_better()
    {
        var report = ExperimentJudge.Judge(Stop, NineFolds(0.1, 0.3), new ExperimentOptions(NoHoldOut: true)).Value;

        report.Outcome.ShouldBe(ExperimentOutcome.Inconclusive);
        report.Reasons.ShouldContain(r => r.Contains("no hold-out"));
    }

    [Theory]
    [InlineData("4", "4.0", true)]
    [InlineData("04", "4", true)]
    [InlineData("1e0", "1", true)]
    [InlineData("0", "-0", true)]
    [InlineData("1.2", "1.20", true)]
    [InlineData("4", "4.5", false)]
    [InlineData("fast", "fast", true)]
    [InlineData("fast", "slow", false)]
    public void Numeric_values_are_compared_as_numbers(string a, string b, bool same)
    {
        ExperimentKnob.SameValue(a, b).ShouldBe(same);
        new ExperimentKnob("stop", ExperimentKnobKind.Parameter, a, b).Problems().Any(p => p.Contains("nothing is changed", StringComparison.Ordinal)).ShouldBe(same);
    }
}
