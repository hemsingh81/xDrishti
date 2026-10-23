using System.Collections.Immutable;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Hand tables, fold stability, determinism and no-leak of the learned entry windows (P6-14, ADR 0069).</summary>
public sealed class EntryWindowLearnerTests
{
    private static long Decision(DateOnly d, int minute) => new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds() + ((minute + 1) * 60L);

    /// <summary>A row whose decision bar is the given minute of the day (the decision instant is the end of that bar).</summary>
    private static ValidationRow Row(string key, DateOnly d, int minute, int label, string cell = "A") =>
        new(key, label, label == 1 ? 1.5 : -1, d, Decision(d, minute), Decision(d, minute) + 600, cell, ImmutableArray<double>.Empty);

    private static readonly int B0 = 9 * 60 + 15;

    private static readonly int B1 = B0 + 30;

    private static readonly int B2 = B0 + 60;

    private static readonly DateOnly Day = new(2025, 1, 6);

    /// <summary>Trades in one bucket: <paramref name="n"/> of them, the first <paramref name="wins"/> winners.</summary>
    private static IEnumerable<ValidationRow> Bucket(string tag, int minute, int n, int wins, string cell = "A") =>
        Enumerable.Range(0, n).Select(i => Row($"{cell}-{tag}-{i}", Day.AddDays(i), minute, i < wins ? 1 : 0, cell));

    private static EntryWindowOptions Plain(int minTrades = 10, double lift = 0.03) => new(30, minTrades, lift, 0);

    private static EntryWindowReport Learn(IEnumerable<ValidationRow> rows, EntryWindowOptions? o = null, ValidationOptions? v = null) =>
        EntryWindowLearner.Learn([.. rows], o ?? Plain(), v ?? new ValidationOptions()).Value;

    [Fact]
    public void Hand_table_picks_the_strongest_bucket_and_states_the_evidence()
    {
        var rows = Bucket("a", B0, 20, 16).Concat(Bucket("b", B1, 20, 10)).Concat(Bucket("c", B2, 20, 4));

        var p = Learn(rows).Cells.Single();

        p.CellWinRate!.Value.ShouldBe(0.5, 1e-12);
        p.WindowStartMinute.ShouldBe(B0);
        p.WindowEndMinute.ShouldBe(B0 + 30);
        p.WindowTrades.ShouldBe(20);
        p.WindowWins.ShouldBe(16);
        p.ShrunkRate!.Value.ShouldBe(0.8, 1e-12);
        p.Lift!.Value.ShouldBe(0.3, 1e-12);
        p.KeptShare.ShouldBe(1.0 / 3, 1e-12);
        p.RemovesMajority.ShouldBeTrue();
        p.MeanRInside!.Value.ShouldBe(((16 * 1.5) - 4) / 20, 1e-12);
        p.Status.ShouldBe(EntryWindowStatus.Inconclusive); // no fold fits in a few weeks of data
        p.Buckets.Count.ShouldBe(13);
        p.Buckets[1].ShrunkRate.ShouldBe(0.5, 1e-12);
    }

    [Fact]
    public void The_prior_pulls_a_small_window_toward_the_cell_rate()
    {
        var rows = Bucket("a", B0, 10, 9).Concat(Bucket("b", B1, 30, 12)); // cell 21/40 = 0.525

        var p = Learn(rows, new EntryWindowOptions(30, 10, 0.03, 20)).Cells.Single();

        p.WindowStartMinute.ShouldBe(B0);
        p.ShrunkRate!.Value.ShouldBe((9 + (20 * 0.525)) / 30, 1e-12);
    }

    [Fact]
    public void A_cell_with_too_few_trades_has_not_enough_evidence()
    {
        var p = Learn(Bucket("a", B0, 6, 6)).Cells.Single();

        p.Status.ShouldBe(EntryWindowStatus.NotEnoughEvidence);
        p.WindowStartMinute.ShouldBeNull();
        p.Reasons.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_flat_cell_has_no_stable_window()
    {
        var rows = Bucket("a", B0, 20, 10).Concat(Bucket("b", B1, 20, 10));

        var p = Learn(rows).Cells.Single();

        p.Status.ShouldBe(EntryWindowStatus.NoStableWindow);
        p.WindowStartMinute.ShouldBeNull();
    }

    [Fact]
    public void A_window_that_needs_a_weak_bucket_inside_is_refused()
    {
        // Pooled B0+B1 would read 26/30 against 26/31 overall, but B1 (0.6) is below the cell rate: a hole.
        var rows = Bucket("a", B0, 20, 20).Concat(Bucket("b", B1, 10, 6)).Concat(Bucket("c", B2, 1, 0));

        var p = Learn(rows, Plain(25, 0.01)).Cells.Single();

        p.Status.ShouldBe(EntryWindowStatus.NoStableWindow);
        p.WindowStartMinute.ShouldBeNull();
    }

    [Fact]
    public void Trades_outside_the_session_are_counted_and_not_bucketed()
    {
        var rows = Bucket("a", B0, 20, 16).Concat(Bucket("b", B1, 20, 10)).Append(Row("late", Day, 18 * 60, 1));

        var report = Learn(rows);

        report.OutsideSession.ShouldBe(1);
        report.Cells.Single().CellTrades.ShouldBe(40);
        report.Limits.ShouldContain(l => l.Contains("outside the regular session"));
    }

    /// <summary>About 14 months of weekdays, three buckets per day; the labels per bucket come from <paramref name="label"/>(day index, bucket, date).</summary>
    private static List<ValidationRow> Series(Func<int, int, DateOnly, int> label, string cell = "A")
    {
        var rows = new List<ValidationRow>();
        var d = new DateOnly(2025, 1, 2);
        var idx = 0;
        while (d <= new DateOnly(2026, 3, 13))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                var minutes = new[] { B0, B1, B2 };
                for (var b = 0; b < 3; b++)
                {
                    rows.Add(Row($"{cell}-{idx}-{b}", d, minutes[b] + 3, label(idx, b, d), cell));
                }

                idx++;
            }

            d = d.AddDays(1);
        }

        return rows;
    }

    private static int Steady(int i, int b, DateOnly d) => b switch
    {
        0 => i % 5 != 0 ? 1 : 0, // 80 %
        1 => i % 2,              // 50 %
        _ => i % 5 == 0 ? 1 : 0, // 20 %
    };

    private static readonly ValidationOptions TwoFoldsEnough = new(MinFoldsForVerdict: 2);

    [Fact]
    public void A_window_that_holds_in_every_fold_is_proposed_when_the_folds_suffice()
    {
        var p = Learn(Series(Steady), Plain(30), TwoFoldsEnough).Cells.Single();

        p.WindowStartMinute.ShouldBe(B0);
        p.WindowEndMinute.ShouldBe(B0 + 30);
        p.FoldsEvaluable.ShouldBe(2);
        p.FoldsHeld.ShouldBe(2);
        p.FoldsRequired.ShouldBe(2);
        p.Status.ShouldBe(EntryWindowStatus.Proposed);
        p.Folds.ShouldAllBe(f => f.Held == true && f.WindowStartMinute == B0);
    }

    [Fact]
    public void Few_folds_cap_a_passing_window_at_inconclusive()
    {
        var report = Learn(Series(Steady), Plain(30));

        report.UsedFolds.ShouldBe(2);
        report.Cells.Single().Status.ShouldBe(EntryWindowStatus.Inconclusive);
        report.Limits.ShouldContain(l => l.Contains("Fewer than 6 folds"));
    }

    [Fact]
    public void A_window_that_only_worked_in_train_is_not_stable()
    {
        static int Fading(int i, int b, DateOnly d) => b == 0 ? (d < new DateOnly(2025, 10, 1) ? (i % 10 != 0 ? 1 : 0) : i % 2) : i % 2;

        var p = Learn(Series(Fading), Plain(30), TwoFoldsEnough).Cells.Single();

        p.WindowStartMinute.ShouldBe(B0); // in-sample it looks good
        p.FoldsHeld.ShouldBe(0);
        p.Status.ShouldBe(EntryWindowStatus.NoStableWindow);
    }

    [Fact]
    public void Same_rows_in_any_order_give_the_same_text()
    {
        var rows = Series(Steady);
        var a = Learn(rows, Plain(30), TwoFoldsEnough).ToText();
        var b = Learn(Enumerable.Reverse(rows), Plain(30), TwoFoldsEnough).ToText();

        b.ShouldBe(a);
        a.ShouldContain("Proposed");
    }

    [Fact]
    public void An_earlier_fold_does_not_see_later_labels()
    {
        var rows = Series(Steady);
        var altered = rows.Select(r => r.TradeDate >= new DateOnly(2026, 2, 1) ? r with { Label = 1 - r.Label } : r).ToList();

        var before = Learn(rows, Plain(30), TwoFoldsEnough).Cells.Single().Folds[0];
        var after = Learn(altered, Plain(30), TwoFoldsEnough).Cells.Single().Folds[0];

        after.ShouldBe(before);
    }

    [Fact]
    public void A_train_row_whose_horizon_reaches_the_test_window_is_purged()
    {
        var rows = Series(Steady);
        var first = rows.Where(r => r.TradeDate >= new DateOnly(2025, 11, 1)).Min(r => r.DecisionEpochSeconds);
        var late = rows.First(r => r.TradeDate < new DateOnly(2025, 10, 20));
        var altered = rows.Select(r => r.Key == late.Key ? r with { ResolvedEpochSeconds = first + 10 } : r).ToList();

        var withLong = Learn(altered, Plain(30), TwoFoldsEnough).Cells.Single().Folds[0].TrainCount;
        var plain = Learn(rows, Plain(30), TwoFoldsEnough).Cells.Single().Folds[0].TrainCount;

        withLong.ShouldBe(plain - 1);
    }

    [Fact]
    public void Cells_are_learnt_separately_and_listed_by_key()
    {
        var rows = Series(Steady, "B").Concat(Series((i, b, d) => b == 2 ? (i % 5 != 0 ? 1 : 0) : i % 2, "A"));

        var cells = Learn(rows, Plain(30), TwoFoldsEnough).Cells;

        cells.Select(c => c.CellKey).ShouldBe(["A", "B"]);
        cells[0].WindowStartMinute.ShouldBe(B2);
        cells[1].WindowStartMinute.ShouldBe(B0);
    }

    [Fact]
    public void Bad_input_is_a_failure_with_a_code()
    {
        var one = Bucket("a", B0, 20, 10).ToList();

        EntryWindowLearner.Learn([], Plain(), new ValidationOptions()).Error.Code.ShouldBe("entry_windows.no_rows");
        EntryWindowLearner.Learn(one, new EntryWindowOptions(BucketMinutes: 1), new ValidationOptions()).Error.Code.ShouldBe("entry_windows.options");
        EntryWindowLearner.Learn([.. one, one[0]], Plain(), new ValidationOptions()).Error.Code.ShouldBe("entry_windows.duplicate_key");
        EntryWindowLearner.Learn([one[0] with { Label = 2 }], Plain(), new ValidationOptions()).Error.Code.ShouldBe("entry_windows.bad_label");
        EntryWindowLearner.Learn(one, Plain(), new ValidationOptions(HoldOutStart: Day)).Error.Code.ShouldBe("entry_windows.holdout_touched");
    }
}
