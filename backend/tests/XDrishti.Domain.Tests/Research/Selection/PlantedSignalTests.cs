using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research.Selection;

/// <summary>Criteria 5 and 6 of P4-06: a planted exit wins every fold and the cell is called stable; a noise-only series is called unstable at a measured rate; the seed moves only the trades and lambda only the scores.</summary>
public sealed class PlantedSignalTests
{
    /// <summary>Measured on 2026-10-22 over seeds 1 to 1,000 of a six-candidate noise line: the share of series reported not stable. The test fixes a floor under the measured value, so a rule that gets more lenient fails it.</summary>
    private const double NoiseUnstableFloor = 0.99;

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_planted_exit_wins_every_fold_and_is_the_final_choice(int holdSessions)
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var candidates = SelectionSeries.Line(5);
            var planted = candidates[2].Id;

            var result = ExitSelector.Select(SelectionSeries.Input(seed, candidates, [0, 0, 0.5, 0, 0], holdSessions: holdSessions));

            result.Folds.Count.ShouldBeGreaterThanOrEqualTo(4);
            result.Folds.ShouldAllBe(f => f.Winner == planted, "seed " + seed);
            result.FinalCandidate.ShouldBe(planted);
            result.Chosen.ShouldBe(planted);
            result.Stability.Stable.ShouldBe(true, "seed " + seed + ": " + result.Stability.Reason);
            result.Stability.Reason.ShouldStartWith("stable");
        }
    }

    [Fact]
    public void The_pooled_out_of_sample_result_of_the_walk_forward_is_the_planted_candidates_when_it_wins_every_fold()
    {
        var candidates = SelectionSeries.Line(5);

        var result = ExitSelector.Select(SelectionSeries.Input(2, candidates, [0, 0, 0.5, 0, 0]));

        var walk = result.Pooled.Single(p => p.Name == "walk-forward");
        var planted = result.Pooled.Single(p => p.Name == candidates[2].Id);
        walk.Trades.ShouldBe(planted.Trades);
        walk.Score.ShouldBe(planted.Score);
        walk.Score!.Value.ShouldBeGreaterThan(result.Pooled.Single(p => p.Name == "default " + candidates[0].Id).Score!.Value);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void A_noise_only_series_that_loses_on_average_is_reported_unstable_on_at_least_the_measured_share_of_a_thousand_seeds(int count)
    {
        var candidates = SelectionSeries.Line(count);
        var notStable = 0;
        for (var seed = 1; seed <= 1000; seed++)
        {
            if (ExitSelector.Select(SelectionSeries.Input(seed, candidates, new double[count])).Stability.Stable != true)
            {
                notStable++;
            }
        }

        // zero edge: every candidate's score is negative by the downside penalty, so rule (b) refuses the verdict
        (notStable / 1000.0).ShouldBeGreaterThanOrEqualTo(NoiseUnstableFloor, "measured " + notStable + " of 1000");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void A_profitable_series_in_which_the_exit_does_not_matter_is_often_reported_stable_and_the_report_then_says_how_likely_that_is_by_chance(int count)
    {
        // Measured on 2026-10-22: every candidate drawn from one profitable distribution passes the agreement rule on most series (neighbours count as agreement), so the verdict alone does not separate an exit that matters from one that does not.
        // What the report does say is the exact chance of passing by luck beside it; this test pins both so the weakness cannot disappear from the report unnoticed.
        var candidates = SelectionSeries.Line(count);
        var stable = new List<double>();
        for (var seed = 1; seed <= 1000; seed++)
        {
            var result = ExitSelector.Select(SelectionSeries.Input(seed, candidates, Enumerable.Repeat(0.8, count).ToArray()));
            if (result.Stability.Stable == true)
            {
                stable.Add(result.Stability.ChancePass!.Value);
                result.Banner.ShouldContain(l => l.Contains("can pass by chance about", StringComparison.Ordinal));
            }
        }

        stable.Count.ShouldBeGreaterThanOrEqualTo(900, "stable on " + stable.Count + " of 1000 equal-exit series");
        stable.Order().ElementAt(stable.Count / 2).ShouldBeGreaterThanOrEqualTo(count == 3 ? 0.5 : 0.7, "the median chance of passing by luck among them");
    }

    [Fact]
    public void A_losing_exit_that_keeps_winning_a_fold_is_a_stable_loss_not_a_stable_edge()
    {
        // every candidate loses on average; rule (b) must keep the verdict at unstable even when the winners agree
        var candidates = SelectionSeries.Line(3);

        var verdicts = Enumerable.Range(1, 30).Select(seed => ExitSelector.Select(SelectionSeries.Input(seed, candidates, [-0.4, -0.2, -0.4])).Stability).ToList();

        verdicts.ShouldAllBe(s => s.Stable != true || s.PositiveOutOfSample * 100.0 >= 60 * s.Agreeing);
        verdicts.Any(s => s.Reason.Contains("stable loss", StringComparison.Ordinal)).ShouldBeTrue("the rule (b) message is seen at least once");
    }

    [Fact]
    public void Changing_the_seed_changes_only_the_trades_and_not_the_geometry_of_the_folds()
    {
        var candidates = SelectionSeries.Line(3);

        var a = ExitSelector.Select(SelectionSeries.Input(1, candidates, [0, 0.3, 0]));
        var b = ExitSelector.Select(SelectionSeries.Input(2, candidates, [0, 0.3, 0]));

        a.Folds.Select(f => (f.TestStart, f.TestEnd, f.TrainCutoff, f.TrainTrades, f.TestTrades, f.Embargoed, f.Purged)).ShouldBe(b.Folds.Select(f => (f.TestStart, f.TestEnd, f.TrainCutoff, f.TrainTrades, f.TestTrades, f.Embargoed, f.Purged)));
        a.Folds.Select(f => f.WinnerTrainScore).ShouldNotBe(b.Folds.Select(f => f.WinnerTrainScore));
    }

    [Fact]
    public void Changing_lambda_changes_the_scores_and_nothing_in_the_folds()
    {
        var half = SelectionSeries.Input(6, SelectionSeries.Line(3), [0, 0.3, 0]);
        var zero = half with { Options = half.Options with { Lambda = 0 } };

        var a = ExitSelector.Select(half);
        var b = ExitSelector.Select(zero);

        a.Folds.Select(f => (f.TestStart, f.TestEnd, f.TrainCutoff, f.TrainTrades, f.TestTrades, f.Embargoed, f.Purged)).ShouldBe(b.Folds.Select(f => (f.TestStart, f.TestEnd, f.TrainCutoff, f.TrainTrades, f.TestTrades, f.Embargoed, f.Purged)));
        a.Folds[0].Rows[0].TrainScore.ShouldNotBe(b.Folds[0].Rows[0].TrainScore);
        b.Folds[0].Rows[0].TrainScore!.Value.ShouldBe(b.Folds[0].Rows[0].TrainMeanR!.Value, 1e-12, "at lambda 0 the score is the mean");
    }

    [Fact]
    public void Identical_inputs_give_identical_records()
    {
        var a = ExitSelector.Select(SelectionSeries.Input(9, SelectionSeries.Line(4), [0, 0.2, 0, 0]));
        var b = ExitSelector.Select(SelectionSeries.Input(9, SelectionSeries.Line(4), [0, 0.2, 0, 0]));

        Flatten(b).ShouldBe(Flatten(a));
        b.Pooled.ShouldBe(a.Pooled);
        b.Stability.ShouldBe(a.Stability);
        b.Banner.ShouldBe(a.Banner);
    }

    private static List<object?> Flatten(ExitSelection s) =>
        [.. s.Folds.SelectMany(f => new object?[] { f.Index, f.TestStart, f.TestEnd, f.TrainCutoff, f.TrainTrades, f.TestTrades, f.Embargoed, f.Purged, f.Winner, f.OnPlateau, f.Isolated, f.LonePeak, f.WinnerTrainScore, f.WinnerTestTrades, f.WinnerTestScore, f.Thin }.Concat(f.Rows.Cast<object?>()))];

    [Fact]
    public void The_chance_of_passing_by_luck_is_exact_on_small_cases()
    {
        // 2 candidates that are neighbours, 4 folds, rule 60 %: the agreement count is the whole of the 4 folds, so it always passes
        int[][] twoNeighbours = [[1], [0]];
        // 3 candidates that are not neighbours, 3 folds: the modal winner needs 2 of 3 folds (66.7 %) => P(some candidate wins >= 2 of 3) = 1 - 3!/3^3 = 1 - 6/27
        int[][] none = [[], [], []];

        ExitSelector.ChancePass(4, 2, twoNeighbours, 60)!.Value.ShouldBe(1.0, 1e-12);
        ExitSelector.ChancePass(3, 3, none, 60)!.Value.ShouldBe(21.0 / 27, 1e-12);
        // 2 candidates, no neighbours, 5 folds: modal count >= 3 always (3 of 5 = 60 %) => 1; 6 folds: needs >= 3.6 => 4 of 6: 1 - P(3-3) = 1 - 20/64
        ExitSelector.ChancePass(5, 2, [[], []], 60)!.Value.ShouldBe(1.0, 1e-12);
        ExitSelector.ChancePass(6, 2, [[], []], 60)!.Value.ShouldBe(44.0 / 64, 1e-12);
        ExitSelector.ChancePass(60, 40, none, 60).ShouldBeNull("too many tallies to enumerate is said, not guessed");
    }

    [Fact]
    public void The_banner_carries_the_in_sample_line_the_fold_count_and_the_chance_of_passing_by_luck()
    {
        var result = ExitSelector.Select(SelectionSeries.Input(4, SelectionSeries.Line(4), [0, 0.5, 0, 0]));

        result.Banner.ShouldContain(ExitSelection.InSampleLine(result.FirstEntry, result.LastEntry));
        result.Banner.ShouldContain(l => l.Contains("folds only", StringComparison.Ordinal));
        result.Banner.ShouldContain(l => l.Contains("can pass by chance about", StringComparison.Ordinal));
        result.Stability.ChancePass.ShouldNotBeNull();
        result.Stability.ChancePass!.Value.ShouldBeInRange(0, 1);
    }

    [Fact]
    public void The_in_sample_line_derives_the_span_from_the_first_and_last_entry()
    {
        ExitSelection.InSampleLine(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31)).ShouldContain("2.0 years, 2024-01-01 to 2025-12-31");
        ExitSelection.InSampleLine(null, null).ShouldContain("no entries");
    }

    [Fact]
    public void An_unstable_verdict_with_a_choice_set_says_so_in_the_banner()
    {
        var result = ExitSelector.Select(SelectionSeries.Input(4, SelectionSeries.Line(4), [0, 0.5, 0, 0]));

        (result.Stability.Stable == false && result.FinalCandidate is not null).ShouldBe(result.Banner.Any(l => l.StartsWith("chosen but unstable", StringComparison.Ordinal)));
        (result.Stability.ChancePass is null).ShouldBe(result.Banner.Contains("chance-pass not computed"));
    }
}
