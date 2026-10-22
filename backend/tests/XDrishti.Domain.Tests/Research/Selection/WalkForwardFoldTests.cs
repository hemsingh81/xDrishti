using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research.Selection;

/// <summary>Criteria 2, 4 and 6 of P4-06: the folds, the embargo, the purge of open swing trades and the minimum sample equal tables worked out by hand.</summary>
public sealed class WalkForwardFoldTests
{
    private static readonly SelectionOptions Defaults = new();

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void The_windows_of_a_two_year_run_equal_the_hand_table()
    {
        // first entry 2024-04-01, +9 months = 2025-01-01 (already a month start); last entry 2026-04-30 => six windows, the last one month long
        var (windows, dropped) = ExitSelector.Windows(D(2024, 4, 1), D(2026, 4, 30), Defaults);

        windows.ShouldBe([(D(2025, 1, 1), D(2025, 3, 31)), (D(2025, 4, 1), D(2025, 6, 30)), (D(2025, 7, 1), D(2025, 9, 30)), (D(2025, 10, 1), D(2025, 12, 31)), (D(2026, 1, 1), D(2026, 3, 31)), (D(2026, 4, 1), D(2026, 6, 30))]);
        dropped.ShouldBe(0);
    }

    [Fact]
    public void A_final_window_shorter_than_a_month_is_dropped_and_counted()
    {
        var (windows, dropped) = ExitSelector.Windows(D(2024, 4, 1), D(2026, 4, 20), Defaults);

        windows.Count.ShouldBe(5);
        windows[^1].ShouldBe((D(2026, 1, 1), D(2026, 3, 31)));
        dropped.ShouldBe(1);
    }

    [Fact]
    public void The_first_test_window_starts_at_the_next_month_start_when_the_minimum_ends_inside_a_month()
    {
        // 2024-04-15 + 9 months = 2025-01-15 => 2025-02-01
        var (windows, _) = ExitSelector.Windows(D(2024, 4, 15), D(2026, 4, 30), Defaults);

        windows[0].ShouldBe((D(2025, 2, 1), D(2025, 4, 30)));
    }

    [Fact]
    public void A_window_shorter_than_the_minimum_train_gives_no_fold_and_no_stability_statement()
    {
        var input = SelectionSeries.Input(3, SelectionSeries.Line(3), [0, 0, 0], new SelectionOptions(MinTrainMonths: 36));

        var result = ExitSelector.Select(input);

        result.Folds.ShouldBeEmpty();
        result.Stability.Stable.ShouldBeNull();
        result.Stability.Reason.ShouldContain("no stability statement");
        result.NoChoiceReason.ShouldNotBeNull();
        result.Chosen.ShouldBe(result.Default);
    }

    [Fact]
    public void The_embargo_and_the_purge_count_the_trades_the_calendar_says()
    {
        // test window 2025-01-01 (a Wednesday); 2024-12-25 is a holiday. Sessions before the window, latest first: 12-31, 12-30, 12-27, 12-26, 12-24: the fifth is 12-24, so a train trade may enter on 12-24 at the latest.
        var holidays = new HashSet<DateOnly> { D(2024, 12, 25) };
        var sessions = SelectionSeries.Sessions(D(2024, 3, 1), D(2025, 3, 31), holidays);
        var candidates = SelectionSeries.Line(2);
        var trades = new List<SelectionTrade>();
        foreach (var c in candidates)
        {
            trades.Add(new SelectionTrade(c.Id, D(2024, 4, 1), D(2024, 4, 1), 1));
            trades.Add(new SelectionTrade(c.Id, D(2024, 12, 24), D(2024, 12, 24), 1)); // train: the fifth session before
            trades.Add(new SelectionTrade(c.Id, D(2024, 12, 24), D(2025, 1, 2), 1)); // past the embargo but still open at the window start: purged
            trades.Add(new SelectionTrade(c.Id, D(2024, 12, 26), D(2024, 12, 26), 1)); // inside the embargo
            trades.Add(new SelectionTrade(c.Id, D(2024, 12, 31), D(2024, 12, 31), 1)); // inside the embargo
            trades.Add(new SelectionTrade(c.Id, D(2025, 1, 1), D(2025, 1, 1), 1)); // test
            trades.Add(new SelectionTrade(c.Id, D(2025, 2, 3), D(2025, 2, 3), 1)); // test, and makes the window a full month long
        }

        var result = ExitSelector.Select(new SelectionInput("cell", "RFilled", false, new string('a', 64), candidates, trades, sessions, new SelectionOptions(MinTrades: 2)));

        var fold = result.Folds.Single();
        fold.TestStart.ShouldBe(D(2025, 1, 1));
        fold.TrainCutoff.ShouldBe(D(2024, 12, 24));
        fold.TrainTrades.ShouldBe(4);
        fold.TestTrades.ShouldBe(4);
        fold.Embargoed.ShouldBe(4);
        fold.Purged.ShouldBe(2);
        result.Banner.ShouldContain(l => l.Contains("1 folds only"));
    }

    [Fact]
    public void A_zero_embargo_trains_up_to_the_day_before_the_window()
    {
        var input = SelectionSeries.Input(4, SelectionSeries.Line(2), [0, 0], new SelectionOptions(EmbargoSessions: 0));

        var fold = ExitSelector.Select(input).Folds[0];

        fold.TrainCutoff.ShouldBe(fold.TestStart.AddDays(-1));
        fold.Embargoed.ShouldBe(0);
    }

    [Fact]
    public void A_candidate_below_the_minimum_is_excluded_and_a_fold_with_one_qualified_candidate_is_no_choice()
    {
        var candidates = SelectionSeries.Line(2);
        var sessions = SelectionSeries.Sessions(D(2024, 3, 1), D(2025, 4, 30));
        var trades = new List<SelectionTrade>();
        for (var i = 0; i < 80; i++)
        {
            trades.Add(new SelectionTrade(candidates[0].Id, sessions[i + 20], sessions[i + 20], 0.1));
        }

        for (var i = 0; i < 10; i++)
        {
            trades.Add(new SelectionTrade(candidates[1].Id, sessions[i + 20], sessions[i + 20], 0.2));
        }

        trades.Add(new SelectionTrade(candidates[0].Id, D(2025, 4, 1), D(2025, 4, 1), 0.1));
        trades.Add(new SelectionTrade(candidates[1].Id, D(2025, 4, 1), D(2025, 4, 1), 0.1));

        var result = ExitSelector.Select(new SelectionInput("cell", "RFilled", false, new string('a', 64), candidates, trades, sessions, new SelectionOptions(MinTrainMonths: 4)));

        result.Folds.ShouldNotBeEmpty();
        result.Folds.ShouldAllBe(f => f.Winner == null && f.Thin);
        result.Folds.SelectMany(f => f.Rows).Where(r => r.Id == candidates[1].Id && r.TrainTrades > 0).ShouldAllBe(r => !r.Qualified);
        result.FinalCandidate.ShouldBeNull();
        result.NoChoiceReason.ShouldNotBeNull();
        result.Chosen.ShouldBe(candidates[0].Id, "no choice uses the default, the first candidate");
        result.Banner.ShouldContain(l => l.StartsWith("no choice:", StringComparison.Ordinal) && l.Contains("default exit is used"));
    }

    [Fact]
    public void A_thin_fold_is_out_of_the_stability_fraction()
    {
        // a series in which the test windows hold about 63 trades; a minimum of 200 makes every test window thin although the train sets qualify
        var thick = SelectionSeries.Input(5, SelectionSeries.Line(3), [0, 0.5, 0], new SelectionOptions(MinTrades: 30));
        var thin = thick with { Options = new SelectionOptions(MinTrades: 100, MinTrainMonths: 15) };

        var result = ExitSelector.Select(thin);

        result.Folds.ShouldNotBeEmpty();
        result.Folds.Where(f => f.Winner is not null).ShouldAllBe(f => f.Thin);
        result.Stability.Stable.ShouldBeNull();
        result.FinalCandidate.ShouldBeNull();
        ExitSelector.Select(thick).Folds.Count(f => !f.Thin).ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Moving_the_embargo_by_one_session_changes_only_the_folds_that_have_a_trade_on_that_session()
    {
        // one trade every third session, so some folds have a trade on the session the longer embargo drops and some do not
        var sessions = SelectionSeries.Sessions(SelectionSeries.From, SelectionSeries.To.AddDays(21));
        var candidates = SelectionSeries.Line(2);
        var random = new Random(8);
        var trades = new List<SelectionTrade>();
        for (var d = 0; d < sessions.Count && sessions[d] <= SelectionSeries.To; d += 3)
        {
            foreach (var c in candidates)
            {
                trades.Add(new SelectionTrade(c.Id, sessions[d], sessions[d], SelectionSeries.Normal(random)));
            }
        }

        SelectionInput Of(int embargo) => new("cell", "RFilled", false, new string('a', 64), candidates, trades, sessions, new SelectionOptions(MinTrades: 5, EmbargoSessions: embargo));
        var five = ExitSelector.Select(Of(5));
        var six = ExitSelector.Select(Of(6));

        six.Folds.Count.ShouldBe(five.Folds.Count);
        var untouched = 0;
        var touched = 0;
        for (var i = 0; i < five.Folds.Count; i++)
        {
            (six.Folds[i].TestStart, six.Folds[i].TestEnd, six.Folds[i].TestTrades).ShouldBe((five.Folds[i].TestStart, five.Folds[i].TestEnd, five.Folds[i].TestTrades));
            var dropped = trades.Count(t => t.Entry == five.Folds[i].TrainCutoff);
            if (dropped == 0)
            {
                six.Folds[i].Rows.ShouldBe(five.Folds[i].Rows, "no trade on the session the sixth session drops: the fold is untouched");
                untouched++;
            }
            else
            {
                (five.Folds[i].TrainTrades - six.Folds[i].TrainTrades).ShouldBe(dropped);
                touched++;
            }
        }

        untouched.ShouldBeGreaterThan(0);
        touched.ShouldBeGreaterThan(0);
    }
}
