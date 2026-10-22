using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research.Selection;

/// <summary>Criterion 7 of P4-06 (D11 a, b, c, d, e): no look-ahead in the choice (with a negative control that must fail), fold geometry, candidate-order independence, determinism and scale invariance.</summary>
public sealed class SelectionPropertyTests
{
    private static readonly int[] Seeds = [1, 2, 3];

    private static readonly int[] SeedsGeometry = [4, 5];

    private static SelectionInput Swing(int seed) => SelectionSeries.Input(seed, SelectionSeries.Line(4), [0, 0.3, 0, 0.1], holdSessions: 4);

    private static string?[] Winners(SelectionInput input) => [.. ExitSelector.Select(input).Folds.Select(f => f.Winner)];

    /// <summary>The winners of a chooser that scores on the train trades and also on the test window (the leak the property must catch).</summary>
    private static string?[] LeakyWinners(SelectionInput input)
    {
        var folds = ExitSelector.Select(input).Folds;
        return [.. folds.Select(f => input.Trades.Where(t => t.Entry <= f.TestEnd && (t.Entry >= f.TestStart || (t.Entry <= f.TrainCutoff && t.Exit < f.TestStart))).GroupBy(t => t.CandidateId).OrderByDescending(g => g.Average(t => t.R)).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key)];
    }

    private static SelectionInput Perturbed(SelectionInput input, DateOnly testStart, int mode) => input with
    {
        Trades = mode switch
        {
            0 => [.. input.Trades.Select(t => t.Exit >= testStart ? t with { R = t.R + (t.CandidateId.EndsWith("{rr=3}", StringComparison.Ordinal) ? 50 : -50) } : t)],
            1 => [.. input.Trades.Where(t => t.Exit < testStart || t.Entry.Day % 2 == 0)],
            _ => [.. input.Trades.Where(t => t.Exit < testStart).Concat(input.Trades.Where(t => t.Exit >= testStart).Reverse())],
        },
    };

    /// <summary>For every fold i and every perturbation of the trades that exit on or after the start of fold i, the winners of folds 1 to i are unchanged.</summary>
    private static bool NoLookAhead(SelectionInput input, Func<SelectionInput, string?[]> chooser)
    {
        var baseline = chooser(input);
        var starts = ExitSelector.Select(input).Folds.Select(f => f.TestStart).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            for (var mode = 0; mode < 3; mode++)
            {
                if (!chooser(Perturbed(input, starts[i], mode)).Take(i + 1).SequenceEqual(baseline.Take(i + 1)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    [Fact]
    public void The_winner_of_a_fold_does_not_change_when_any_trade_that_ends_on_or_after_the_window_start_is_changed_removed_or_reordered()
    {
        foreach (var seed in Seeds)
        {
            NoLookAhead(Swing(seed), Winners).ShouldBeTrue("seed " + seed);
        }
    }

    [Fact]
    public void The_property_fails_for_a_chooser_that_scores_on_the_test_window_the_negative_control()
    {
        var failing = Seeds.Count(seed => !NoLookAhead(Swing(seed), LeakyWinners));

        failing.ShouldBeGreaterThan(0, "the leaky chooser must be caught by the perturbation");
    }

    [Fact]
    public void The_geometry_of_the_folds_holds_on_swing_trades()
    {
        foreach (var seed in SeedsGeometry)
        {
            var input = Swing(seed);
            var result = ExitSelector.Select(input);

            result.Folds.Count.ShouldBeGreaterThan(3);
            for (var i = 0; i < result.Folds.Count; i++)
            {
                var f = result.Folds[i];
                (f.TestEnd >= f.TestStart).ShouldBeTrue();
                if (i > 0)
                {
                    f.TestStart.ShouldBe(result.Folds[i - 1].TestEnd.AddDays(1), "tiled, no gap and no overlap");
                }

                // every train trade ends before the window, enters at least 5 sessions before it and has an entry date inside the data
                var train = input.Trades.Where(t => t.Entry <= f.TrainCutoff && t.Exit < f.TestStart).ToList();
                train.Count.ShouldBe(f.TrainTrades);
                train.ShouldAllBe(t => input.Sessions.Count(s => s >= t.Entry && s < f.TestStart) >= 5);
                input.Trades.Count(t => t.Entry >= f.TestStart && t.Entry <= f.TestEnd).ShouldBe(f.TestTrades);
            }
        }
    }

    [Fact]
    public void Permuting_the_candidates_and_the_trades_changes_no_choice()
    {
        var input = SelectionSeries.Input(7, SelectionSeries.Line(5), [0, 0.2, 0.4, 0.2, 0]);
        var shuffled = input with { Candidates = [.. input.Candidates.Reverse()], Trades = [.. input.Trades.OrderByDescending(t => t.R)] };

        var a = ExitSelector.Select(input);
        var b = ExitSelector.Select(shuffled);

        b.Folds.Select(f => f.Winner).ShouldBe(a.Folds.Select(f => f.Winner));
        b.FinalCandidate.ShouldBe(a.FinalCandidate);
        b.Stability.ShouldBe(a.Stability);
        b.Default.ShouldBe(input.Candidates[^1].Id, "the default is the first candidate as given, which is what a permutation of the order is allowed to change");
    }

    [Fact]
    public void Scaling_every_R_by_a_positive_constant_changes_no_choice()
    {
        var input = SelectionSeries.Input(8, SelectionSeries.Line(4), [0, 0.3, 0, 0]);
        var scaled = input with { Trades = [.. input.Trades.Select(t => t with { R = t.R * 8 })] };

        var a = ExitSelector.Select(input);
        var b = ExitSelector.Select(scaled);

        b.Folds.Select(f => f.Winner).ShouldBe(a.Folds.Select(f => f.Winner));
        b.Folds.Select(f => (f.OnPlateau, f.LonePeak, f.Thin)).ShouldBe(a.Folds.Select(f => (f.OnPlateau, f.LonePeak, f.Thin)));
        b.FinalCandidate.ShouldBe(a.FinalCandidate);
        b.Stability.Stable.ShouldBe(a.Stability.Stable);
    }

    [Fact]
    public void A_trade_of_an_unknown_candidate_or_a_single_candidate_is_refused()
    {
        var input = SelectionSeries.Input(1, SelectionSeries.Line(2), [0, 0]);

        Should.Throw<ArgumentException>(() => ExitSelector.Select(input with { Trades = [.. input.Trades, new SelectionTrade("nope", SelectionSeries.From, SelectionSeries.From, 1)] }));
        Should.Throw<ArgumentException>(() => ExitSelector.Select(input with { Candidates = [input.Candidates[0]] }));
        Should.Throw<ArgumentException>(() => ExitSelector.Select(input with { Candidates = [input.Candidates[0], input.Candidates[0]] }));
        Should.Throw<ArgumentException>(() => ExitSelector.Select(input with { Options = new SelectionOptions(Lambda: -1) }));
    }

    [Fact]
    public void Eight_candidates_of_fifty_thousand_trades_each_are_chosen_well_inside_a_second()
    {
        var candidates = SelectionSeries.Line(8);
        var sessions = SelectionSeries.Sessions(SelectionSeries.From, SelectionSeries.To.AddDays(21));
        var random = new Random(11);
        var trades = new List<SelectionTrade>(400_000);
        foreach (var c in candidates)
        {
            for (var i = 0; i < 50_000; i++)
            {
                var day = sessions[i * 520 / 50_000];
                trades.Add(new SelectionTrade(c.Id, day, day, SelectionSeries.Normal(random)));
            }
        }

        var input = new SelectionInput("cell", "RFilled", false, new string('a', 64), candidates, trades, sessions, new SelectionOptions());
        ExitSelector.Select(input);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        var result = ExitSelector.Select(input);

        System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds.ShouldBeLessThan(1.0);
        result.Folds.ShouldNotBeEmpty();
    }
}
