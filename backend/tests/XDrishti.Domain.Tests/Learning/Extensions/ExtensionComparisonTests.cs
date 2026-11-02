using System.Text.Json;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;
using static XDrishti.Domain.Tests.Learning.Extensions.ExtensionKit;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P13-05 criteria 1, 2, 3, 4, 5, 6, 9 and 10: hand values, determinism, row order, A = B, placebo and planted effect, pairing, ledger first, honest states.</summary>
public sealed class ExtensionComparisonTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    private static readonly ExtensionComparisonOptions Tiny = Opts() with { MinTrainMonths = 1, EmbargoSessions = 0, MinTrainTrades = 1, Take = new MetaPolicyOptions(MinKeptShare: 0.5, ThresholdStep: 0.5, MinThresholdRows: 1) };

    /// <summary>Both models read the last feature: A the base value, B the extension value.</summary>
    private sealed class LastFeature : IScorerFactory
    {
        public string Name => "last";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train) => new Read();

        private sealed class Read : IRowScorer
        {
            public double Score(ValidationRow row) => row.Features[^1];
        }
    }

    private sealed class CountingFactory : IScorerFactory
    {
        public int Fits { get; private set; }

        public string Name => "counting";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            Fits++;
            return new Read();
        }

        private sealed class Read : IRowScorer
        {
            public double Score(ValidationRow row) => 0.5;
        }
    }

    private static List<ValidationRow> Hand() =>
    [
        Row("j1", Jan1, 1, 0, 0.5, 0.5),
        Row("j2", Jan1, -1, 1, 0.5, 0.5),
        Row("f1", new DateOnly(2026, 2, 2), -1, 0, 0.2, 0.2),
        Row("f2", new DateOnly(2026, 2, 3), 1, 0, 0.8, 0.8),
        Row("m1", new DateOnly(2026, 3, 2), 1.0, 0, 0.9, 0.9),
        Row("m2", new DateOnly(2026, 3, 3), -1.0, 0, 0.9, 0.1),
        Row("m3", new DateOnly(2026, 3, 4), 2.0, 0, 0.9, 0.9),
        Row("m4", new DateOnly(2026, 3, 5), -0.5, 0, 0.1, 0.9),
    ];

    [Fact]
    public void The_hand_fold_gives_the_four_differences_a_gain_of_one_eighth_and_two_flips()
    {
        var rows = Hand();

        var report = Compare(rows, Group, new LastFeature(), Tiny, Sessions(Jan1, new DateOnly(2026, 3, 31)));

        report.Folds.Count.ShouldBe(2);
        report.Folds[0].Used.ShouldBeTrue();
        report.Folds[0].Evaluable.ShouldBeFalse("the first test fold never has a threshold");
        var march = report.Folds[1];
        march.Evaluable.ShouldBeTrue();
        (march.ThresholdA, march.ThresholdB).ShouldBe((0.5, 0.5));
        march.Candidates.ShouldBe(4);
        march.Flips.ShouldBe(2);
        march.DeltaR.ShouldBe(0.125);
        report.DeltaR.ShouldBe(0.125);
        report.Flips.ShouldBe(2);
        report.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        report.Missing.ShouldContain("folds 1 of 6");
        report.Missing.ShouldContain("paired candidates 4 of 2,000");
    }

    [Fact]
    public void The_first_fold_has_no_gain_but_its_scores_count_in_the_pooled_log_loss_and_brier()
    {
        var report = Compare(Hand(), Group, new LastFeature(), Tiny, Sessions(Jan1, new DateOnly(2026, 3, 31)));

        double[] a = [0.2, 0.8, 0.9, 0.9, 0.9, 0.1];
        double[] b = [0.2, 0.8, 0.9, 0.1, 0.9, 0.9];
        int[] labels = [0, 1, 1, 0, 1, 0];
        var expectedA = MetricsCalculator.Compute(a, labels);
        var expectedB = MetricsCalculator.Compute(b, labels);
        report.DeltaLogLoss.ShouldNotBeNull().ShouldBe(expectedB.LogLoss - expectedA.LogLoss, 1e-12);
        report.DeltaBrier.ShouldNotBeNull().ShouldBe(expectedB.Brier - expectedA.Brier, 1e-12);
        report.FoldsUsed.ShouldBe(2);
        report.FoldsEvaluable.ShouldBe(1);
    }

    [Fact]
    public void The_margins_follow_the_number_of_trials_and_six_folds_need_four_positive()
    {
        MultipleTesting.Factor(1).ShouldBe(1.0);
        (0.02 * MultipleTesting.Factor(4)).ShouldBe(0.03, 1e-12);
        (-0.005 * MultipleTesting.Factor(4)).ShouldBe(-0.0075, 1e-12);
        (0.02 * MultipleTesting.Factor(16)).ShouldBe(0.04, 1e-12);
        BlockBootstrap.Position(0.10, 10_000).ShouldBe(1_000);
        BlockBootstrap.Position(0.90, 10_000).ShouldBe(9_000);
        var o = Opts();

        ExtensionVerdictRule.Decide(Keepable(positive: 4), o).Verdict.ShouldBe(ExtensionVerdict.Keep);
        ExtensionVerdictRule.Decide(Keepable(positive: 3), o).Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
    }

    internal static ExtensionVerdictInput Keepable(int positive = 6, double factor = 1) =>
        new(6, 2_500, 400, positive, 0.05, 0.2, 0.04, 0.2, true, -0.01, -0.004, 3, factor);

    [Fact]
    public void Identical_inputs_give_an_identical_report_and_record_id_for_200_seeds()
    {
        var from = Jan1;
        var to = new DateOnly(2026, 6, 30);
        var sessions = Sessions(from, to.AddDays(5));
        var o = Opts() with { Draws = 200 };
        var recipe = new ProbeFactory(0, 1);
        for (var seed = 0; seed < 200; seed++)
        {
            var rows = Synthetic(seed, from, to, 3, Mode.Placebo);

            var first = Fingerprint(Compare(rows, Group, recipe, o, sessions));
            var second = Fingerprint(Compare(rows, Group, recipe, o, sessions));

            second.ShouldBe(first);
        }
    }

    // Small fixtures keep 200 cases fast: ~5 rows a day over nine months, fewer draws and a lower candidate floor (the verdict table itself is tested row by row elsewhere).
    private static readonly ExtensionComparisonOptions Small = Opts() with { Draws = 200, MinPairedCandidates = 300, MinFlips = 30 };

    [Fact]
    public void Any_permutation_of_the_rows_and_the_sessions_gives_the_same_report_and_record_id_for_200_seeds()
    {
        var to = new DateOnly(2026, 9, 30);
        var sessions = Sessions(Jan1, to.AddDays(5));
        var recipe = new ProbeFactory(0, 1);
        for (var seed = 0; seed < 200; seed++)
        {
            var rows = Synthetic(seed, Jan1, to, 5, Mode.Planted);
            var expected = Fingerprint(Compare(rows, Group, recipe, Small, sessions));
            var random = new SeededRandom((ulong)seed + 99);
            var shuffled = rows.OrderBy(_ => random.Next(1_000_000)).ToList();
            var shuffledSessions = sessions.OrderBy(_ => random.Next(1_000_000)).Concat(sessions.Take(10)).ToList();

            Fingerprint(Compare(shuffled, Group, recipe, Small, shuffledSessions)).ShouldBe(expected, "seed " + seed);
        }
    }

    [Fact]
    public void When_the_extension_adds_nothing_the_gain_is_exactly_zero_and_the_verdict_is_never_keep_for_200_cases()
    {
        var to = new DateOnly(2026, 9, 30);
        var sessions = Sessions(Jan1, to.AddDays(5));
        Mode[] modes = [Mode.Copy, Mode.Constant, Mode.None];
        for (var seed = 0; seed < 200; seed++)
        {
            var mode = modes[seed % 3];
            var trials = (seed / 3) % 2 == 0 ? 1 : 16;
            var rows = Synthetic(seed, Jan1, to, 5, mode);
            // A recipe that ignores the second column for copies, and a constant column adds nothing to the other: B decides as A does.
            IScorerFactory recipe = mode == Mode.Copy ? new ProbeFactory(0) : new ProbeFactory(0, 1);

            var report = Compare(rows, GroupFor(mode), recipe, Small, sessions, trials);

            report.Folds.Where(f => f.Evaluable).ShouldAllBe(f => f.DeltaR == 0 && f.Flips == 0);
            (report.Flips, report.DeltaR).ShouldBe((0, 0.0));
            (report.Stated.Lower, report.Stated.Upper, report.Sensitivity!.Lower, report.Sensitivity.Upper).ShouldBe((0.0, 0.0, 0.0, 0.0));
            report.FoldsEvaluable.ShouldBeGreaterThanOrEqualTo(6);
            report.Verdict.ShouldBe(ExtensionVerdict.Drop, "seed " + seed);
        }
    }

    [Fact]
    public void At_full_size_an_extension_that_adds_nothing_is_a_drop_with_the_default_floors()
    {
        var to = new DateOnly(2026, 9, 30);
        var rows = Synthetic(1, Jan1, to, 21, Mode.Copy);

        var report = Compare(rows, Group, new ProbeFactory(0), Opts(), Sessions(Jan1, to.AddDays(5)), 16);

        report.PairedCandidates.ShouldBeGreaterThanOrEqualTo(2_000);
        report.Verdict.ShouldBe(ExtensionVerdict.Drop);
    }

    [Fact]
    public void With_too_little_data_an_extension_that_adds_nothing_is_not_evaluable_and_never_a_keep()
    {
        var to = new DateOnly(2026, 5, 29);
        var rows = Synthetic(1, Jan1, to, 5, Mode.Constant);

        var report = Compare(rows, Group, new ProbeFactory(0, 1), Opts(), Sessions(Jan1, to.AddDays(5)));

        report.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        report.Missing.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void An_extension_that_is_noise_is_not_kept_for_200_seeds(int trials)
    {
        var to = new DateOnly(2026, 9, 30);
        var sessions = Sessions(Jan1, to.AddDays(5));
        var recipe = new ProbeFactory(0, 1);
        var o = Opts() with { Draws = 300 };
        for (var seed = 0; seed < 200; seed++)
        {
            var rows = Synthetic(seed, Jan1, to, 10, Mode.Placebo);

            Compare(rows, Group, recipe, o, sessions, trials).Verdict.ShouldNotBe(ExtensionVerdict.Keep, "seed " + seed);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void An_extension_that_carries_the_label_is_kept_with_seven_test_folds(int trials)
    {
        var to = new DateOnly(2026, 9, 30);
        var rows = Synthetic(3, Jan1, to, 21, Mode.Planted);

        var report = Compare(rows, Group, new ProbeFactory(0, 1), Opts(), Sessions(Jan1, to.AddDays(5)), trials);

        report.FoldsUsed.ShouldBe(7);
        report.FoldsEvaluable.ShouldBe(6);
        report.Verdict.ShouldBe(ExtensionVerdict.Keep, string.Join("; ", report.Failed.Concat(report.Missing)));
        report.DeltaR.ShouldNotBeNull().ShouldBeGreaterThan(0.2);
    }

    [Fact]
    public void With_six_test_folds_the_same_planted_effect_is_not_evaluable_and_names_folds_five_of_six()
    {
        var to = new DateOnly(2026, 8, 31);
        var rows = Synthetic(3, Jan1, to, 21, Mode.Planted);

        var report = Compare(rows, Group, new ProbeFactory(0, 1), Opts(), Sessions(Jan1, to.AddDays(5)));

        report.FoldsUsed.ShouldBe(6);
        report.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        report.Missing.ShouldContain("folds 5 of 6");
    }

    [Fact]
    public void Model_A_never_sees_a_column_past_the_base_width_and_both_models_get_the_same_train_keys_in_the_same_order()
    {
        var to = new DateOnly(2026, 6, 30);
        var rows = Synthetic(2, Jan1, to, 6, Mode.Planted);
        var recipe = new ProbeFactory(0, 1);

        var report = Compare(rows, Group, recipe, Opts(), Sessions(Jan1, to.AddDays(5)));

        recipe.Fits.Count.ShouldBe(2 * report.FoldsUsed);
        for (var i = 0; i < recipe.Fits.Count; i += 2)
        {
            recipe.Fits[i].Width.ShouldBe(Base);
            recipe.Fits[i + 1].Width.ShouldBe(Base + 1);
            recipe.Fits[i + 1].Keys.ShouldBe(recipe.Fits[i].Keys);
        }

        recipe.ScoredWidths.Distinct().Order().ShouldBe([Base, Base + 1]);
        recipe.ScoredWidths.Count(w => w == Base).ShouldBe(recipe.ScoredWidths.Count(w => w == Base + 1));
    }

    [Fact]
    public void A_trial_that_is_not_in_the_ledger_is_refused_before_any_model_is_fitted()
    {
        var rows = Synthetic(1, Jan1, new DateOnly(2026, 6, 30), 6, Mode.Planted);
        var recipe = new CountingFactory();

        var result = ExtensionComparison.Compare(rows, Base, Group, recipe, ["some-other-trial"], Sessions(Jan1, new DateOnly(2026, 7, 5)), Opts());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.trial_not_in_ledger");
        recipe.Fits.ShouldBe(0);
    }

    [Fact]
    public void The_trial_hash_and_the_ledger_run_id_are_stable_and_change_with_any_listed_field()
    {
        var to = new DateOnly(2026, 6, 30);
        var o = Opts();
        var baseline = ExtensionTrial.Of(Group, "probe", o, Jan1, to);

        ExtensionTrial.Of(Group, "probe", o, Jan1, to).ShouldBe(baseline);
        baseline.Hash.Length.ShouldBe(64);
        baseline.Family.ShouldBe("ext:md-1");
        ExtensionTrial.Of(Group, "probe", o, Jan1, to.AddDays(1)).Hash.ShouldBe(baseline.Hash, "the window is not part of the hash, but of the run id");
        ExtensionTrial.Of(Group, "probe", o, Jan1, to.AddDays(1)).RunId.ShouldNotBe(baseline.RunId);
        var changed = new[]
        {
            ExtensionTrial.Of(new ExtensionGroup("md", "md-1", ["e2"]), "probe", o, Jan1, to),
            ExtensionTrial.Of(new ExtensionGroup("md", "md-2", ["e1"]), "probe", o, Jan1, to),
            ExtensionTrial.Of(Group, "other", o, Jan1, to),
            ExtensionTrial.Of(Group, "probe", o with { RecipeOptionsHash = "r2" }, Jan1, to),
            ExtensionTrial.Of(Group, "probe", o with { LabelRuleVersion = "tb-2" }, Jan1, to),
            ExtensionTrial.Of(Group, "probe", o with { Take = new MetaPolicyOptions(MinKeptShare: 0.6) }, Jan1, to),
            ExtensionTrial.Of(Group, "probe", o with { Unit = FoldUnit.Quarter }, Jan1, to),
            ExtensionTrial.Of(Group, "probe", o with { BlockSessions = 2 }, Jan1, to),
        };
        changed.Select(t => t.Hash).Append(baseline.Hash).Distinct().Count().ShouldBe(changed.Length + 1);
    }

    [Fact]
    public void An_unavailable_value_is_written_as_the_word_and_never_as_a_number()
    {
        var rows = Synthetic(1, Jan1, new DateOnly(2026, 2, 27), 3, Mode.Planted);

        var report = Compare(rows, Group, new ProbeFactory(0, 1), Opts(), Sessions(Jan1, new DateOnly(2026, 2, 27)));
        var record = report.ToRecord("data");

        report.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        report.Stated.Lower.ShouldBeNull();
        using var estimates = JsonDocument.Parse(record.EstimatesJson);
        foreach (var name in new[] { "delta_r", "lower", "upper", "lower_sensitivity", "upper_sensitivity", "delta_log_loss", "delta_brier", "ece_b_pp", "mean_r2_on_flip", "naive_se", "bootstrap_se", "design_effect", "flip_share" })
        {
            var property = estimates.RootElement.GetProperty(name);
            property.ValueKind.ShouldBe(JsonValueKind.String, name);
            property.GetString().ShouldBe("unavailable", name);
        }

        record.Verdict.ShouldBe("NOT_EVALUABLE");
        record.Missing.ShouldContain("lower bound unavailable");
    }

    [Fact]
    public void The_hold_out_is_refused_and_a_row_of_the_wrong_width_too()
    {
        var rows = Hand();
        var o = Tiny with { HoldOutStart = new DateOnly(2026, 3, 5) };
        var hashes = Ledger(Group, new LastFeature(), rows, o, 1);

        var touched = ExtensionComparison.Compare(rows, Base, Group, new LastFeature(), hashes, Sessions(Jan1, new DateOnly(2026, 3, 31)), o);
        var narrow = ExtensionComparison.Compare(rows, 2, Group, new LastFeature(), hashes, [], Tiny);

        touched.Error.Code.ShouldBe("extensions.holdout_touched");
        narrow.Error.Code.ShouldBe("extensions.feature_width");
    }

    [Fact]
    public void A_probability_outside_zero_to_one_is_refused()
    {
        var rows = Hand();
        var bad = Hand().Select(r => r with { Features = [r.Features[0], 1.5] }).ToList();

        var result = ExtensionComparison.Compare(bad, Base, Group, new LastFeature(), Ledger(Group, new LastFeature(), rows, Tiny, 1), Sessions(Jan1, new DateOnly(2026, 3, 31)), Tiny);

        result.Error.Code.ShouldBe("validation.bad_probability");
    }

    [Fact]
    public void The_hold_out_reading_confirms_a_planted_effect_and_is_not_evaluable_under_one_hundred_rows()
    {
        var all = Synthetic(4, Jan1, new DateOnly(2026, 10, 30), 21, Mode.Planted);
        var development = all.Where(r => r.TradeDate < new DateOnly(2026, 10, 1)).ToList();
        var holdOut = all.Where(r => r.TradeDate >= new DateOnly(2026, 10, 1)).ToList();
        var sessions = Sessions(Jan1, new DateOnly(2026, 11, 5));

        var confirmed = ExtensionHoldOutCheck.Check(development, holdOut, Base, Group, new ProbeFactory(0, 1), 0.3, sessions, Opts()).Value;
        var tiny = ExtensionHoldOutCheck.Check(development, holdOut.Take(50).ToList(), Base, Group, new ProbeFactory(0, 1), 0.3, sessions, Opts()).Value;
        var opposite = ExtensionHoldOutCheck.Check(development, holdOut, Base, Group, new ProbeFactory(0, 1), -0.3, sessions, Opts()).Value;

        confirmed.Status.ShouldBe(ExtensionHoldOutStatus.Confirmed);
        confirmed.DeltaR.ShouldNotBeNull().ShouldBeGreaterThan(0);
        confirmed.LogLossB.ShouldNotBeNull().ShouldBeLessThan(confirmed.LogLossA!.Value);
        tiny.Status.ShouldBe(ExtensionHoldOutStatus.NotEvaluable);
        opposite.Status.ShouldBe(ExtensionHoldOutStatus.NotConfirmed);
    }

    [Fact]
    public void The_record_holds_the_family_the_version_key_and_the_hold_out_flag()
    {
        var rows = Synthetic(1, Jan1, new DateOnly(2026, 6, 30), 4, Mode.Planted);
        var report = Compare(rows, Group, new ProbeFactory(0, 1), Opts(), Sessions(Jan1, new DateOnly(2026, 7, 5)), 4);

        var record = report.ToRecord("d".PadLeft(64, 'a'));
        var holdOut = ExtensionHoldOutRecords.Build(report, new ExtensionHoldOutResult(ExtensionHoldOutStatus.NotConfirmed, 150, -0.01, 0.69, 0.70, 0.5, 0.5, null), "d".PadLeft(64, 'a'));

        record.Family.ShouldBe("ext:md-1");
        record.VersionKey.ShouldBe("md-1:e1");
        record.Trials.ShouldBe(4);
        record.Factor.ShouldBe(1.5);
        record.BaseVersion.ShouldBe("tf-1");
        record.FoldUnit.ShouldBe("month");
        holdOut.HoldOutRead.ShouldBeTrue();
        record.HoldOutRead.ShouldBeFalse();
        (holdOut.Family, holdOut.VersionKey, holdOut.TrialHash).ShouldBe((record.Family, record.VersionKey, record.TrialHash));
        holdOut.Verdict.ShouldBe("INCONCLUSIVE", "the hold-out record carries the hold-out outcome, not the development verdict");
        holdOut.Rows.ShouldBe(150);
        holdOut.EstimatesJson.ShouldContain("NotConfirmed");
        holdOut.EvaluationId.ShouldNotBe(record.EvaluationId);
        record.EvaluationId.Length.ShouldBe(64);
    }

    [Fact]
    public void A_confirmed_and_an_unreadable_hold_out_carry_their_own_outcome()
    {
        var rows = Synthetic(1, Jan1, new DateOnly(2026, 6, 30), 4, Mode.Planted);
        var report = Compare(rows, Group, new ProbeFactory(0, 1), Opts(), Sessions(Jan1, new DateOnly(2026, 7, 5)));
        var data = new string('a', 64);

        var confirmed = ExtensionHoldOutRecords.Build(report, new ExtensionHoldOutResult(ExtensionHoldOutStatus.Confirmed, 200, 0.2, 0.69, 0.60, 0.5, 0.5, null), data);
        var unreadable = ExtensionHoldOutRecords.Build(report, new ExtensionHoldOutResult(ExtensionHoldOutStatus.NotEvaluable, 50, null, null, null, null, null, "hold-out rows 50 of 100"), data);

        confirmed.Verdict.ShouldBe("KEEP");
        unreadable.Verdict.ShouldBe("NOT_EVALUABLE");
        unreadable.Missing.ShouldBe(["hold-out rows 50 of 100"]);
        using var estimates = JsonDocument.Parse(unreadable.EstimatesJson);
        estimates.RootElement.GetProperty("delta_r").GetString().ShouldBe("unavailable");
    }

    [Fact]
    public void Eleven_sessions_with_a_holiday_make_eleven_day_blocks_and_three_blocks_of_five_sessions()
    {
        // Rows on the 11 sessions 2026-03-02 to 2026-03-17 (03-04 is a holiday and not in the list); the first fold (February) is not evaluable, March is.
        var march = new[] { 2, 3, 5, 6, 9, 10, 11, 12, 13, 16, 17 };
        var rows = new List<ValidationRow> { Row("j1", Jan1, 1, 0, 0.5, 0.5), Row("j2", Jan1, -1, 1, 0.5, 0.5), Row("f1", new DateOnly(2026, 2, 2), -1, 0, 0.2, 0.2), Row("f2", new DateOnly(2026, 2, 3), 1, 0, 0.8, 0.8) };
        rows.AddRange(march.Select(d => Row("m" + d, new DateOnly(2026, 3, d), d % 2 == 0 ? 1 : -1, 0, 0.9, d % 2 == 0 ? 0.9 : 0.1)));
        var sessions = Sessions(Jan1, new DateOnly(2026, 3, 31)).Where(d => d != new DateOnly(2026, 3, 4)).ToList();

        var report = Compare(rows, Group, new LastFeature(), Tiny, sessions);

        report.PairedCandidates.ShouldBe(11);
        report.Stated.Blocks.ShouldBe(11);
        report.Sensitivity.ShouldNotBeNull().Blocks.ShouldBe(3, "11 consecutive sessions in blocks of 5 are 5 + 5 + 1");
    }

    [Fact]
    public void The_filings_preset_spanning_three_evaluable_quarters_has_three_season_blocks_and_no_sensitivity_run()
    {
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2026, 12, 31);
        var rows = Synthetic(2, from, to, 3, Mode.Planted);
        var options = ExtensionComparisonOptions.ForFilings() with { RecipeOptionsHash = "r1", LabelRuleVersion = "tb-1", Draws = 200 };

        var report = Compare(rows, Group, new ProbeFactory(0, 1), options, Sessions(from, to.AddDays(5)));

        report.Options.Block.ShouldBe(BlockKind.Season);
        report.FoldsUsed.ShouldBe(4);
        report.FoldsEvaluable.ShouldBe(3);
        report.Stated.Blocks.ShouldBe(3);
        report.Sensitivity.ShouldBeNull();
    }

    [Fact]
    public void A_row_dated_outside_the_session_list_or_a_list_too_short_for_the_embargo_is_refused()
    {
        var rows = Synthetic(1, Jan1, new DateOnly(2026, 6, 30), 3, Mode.Planted);
        var all = Sessions(Jan1, new DateOnly(2026, 7, 5));
        var recipe = new CountingFactory();
        var hashes = Ledger(Group, recipe, rows, Opts(), 1);

        var missingDay = ExtensionComparison.Compare(rows, Base, Group, recipe, hashes, [.. all.Where(d => d != new DateOnly(2026, 3, 10))], Opts());
        var strict = Opts() with { EmbargoSessions = 100 };
        var shortList = ExtensionComparison.Compare(rows, Base, Group, recipe, Ledger(Group, recipe, rows, strict, 1), all, strict);

        missingDay.Error.Code.ShouldBe("extensions.sessions");
        shortList.Error.Code.ShouldBe("extensions.sessions");
        recipe.Fits.ShouldBe(0);
    }
}
