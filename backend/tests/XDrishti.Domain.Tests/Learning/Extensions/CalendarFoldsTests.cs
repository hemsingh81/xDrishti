using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;
using static XDrishti.Domain.Tests.Learning.Extensions.ExtensionKit;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P13-05 criterion 7: monthly and quarterly hand calendars, the embargo in sessions, the purge, the incomplete last window, rows in the hold-out.</summary>
public sealed class CalendarFoldsTests
{
    private static ExtensionComparisonOptions Monthly(int embargo = 3) => Opts() with { MinTrainMonths = 2, EmbargoSessions = embargo, MinTrainTrades = 1 };

    // 2026-08-01 is a Saturday: the month of August starts on a weekend. 2026-08-27 and 2026-09-07 are holidays: they are left out of the session list.
    private static readonly DateOnly[] Holidays = [new(2026, 8, 27), new(2026, 9, 7)];

    private static readonly int[] AugustDays = [20, 21, 24, 25, 26, 28, 31];

    private static List<DateOnly> Calendar(DateOnly from, DateOnly to) => [.. Sessions(from, to).Where(d => !Holidays.Contains(d))];

    private static ValidationRow At(string key, DateOnly d, long resolvedOffset = 1800, int minute = 0) =>
        Row(key, d, 1, minute, 0.5) with { ResolvedEpochSeconds = Epoch(d, minute) + resolvedOffset };

    [Fact]
    public void The_first_monthly_window_starts_on_the_first_month_boundary_two_months_after_the_first_row()
    {
        var rows = new[] { At("a", new DateOnly(2026, 5, 20)), At("b", new DateOnly(2026, 7, 3)), At("c", new DateOnly(2026, 8, 3)), At("d", new DateOnly(2026, 9, 2)) };

        var set = CalendarFolds.Cut(rows, Calendar(new DateOnly(2026, 5, 1), new DateOnly(2026, 9, 30)), Monthly());

        // 2026-05-20 + 2 months = 2026-07-20, the first boundary on or after it is 2026-08-01 (a Saturday).
        set.Folds.Select(f => (f.Start, f.End)).ShouldBe([(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))]);
        set.Folds[0].Test.Select(r => r.Key).ShouldBe(["c"]);
        set.Folds[1].Test.Select(r => r.Key).ShouldBe(["d"]);
    }

    [Fact]
    public void A_date_that_is_already_a_boundary_starts_the_window_itself()
    {
        var rows = new[] { At("a", new DateOnly(2026, 1, 1)), At("b", new DateOnly(2026, 3, 2)) };

        var set = CalendarFolds.Cut(rows, Calendar(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)), Monthly());

        set.Folds.Single().Start.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void The_embargo_drops_the_rows_of_the_last_three_sessions_before_the_window_across_a_weekend_and_a_holiday()
    {
        // The window starts on 2026-09-01 (Tuesday). The last three sessions before it are 08-26, 08-28 (08-27 is a holiday) and 08-31 (Monday, after a weekend).
        var days = new[] { new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26), new DateOnly(2026, 8, 28), new DateOnly(2026, 8, 31) };
        var rows = new List<ValidationRow> { At("old", new DateOnly(2026, 6, 1)) };
        rows.AddRange(days.Select(d => At("r" + d.Day, d)));
        rows.Add(At("test", new DateOnly(2026, 9, 2)));

        var set = CalendarFolds.Cut(rows, Calendar(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 30)), Monthly(3));

        var fold = set.Folds.Single(f => f.Start == new DateOnly(2026, 9, 1));
        fold.Train.Select(r => r.Key).ShouldBe(["old", "r25"]);
    }

    [Fact]
    public void An_embargo_of_five_sessions_reaches_back_over_a_holiday()
    {
        var rows = new List<ValidationRow> { At("old", new DateOnly(2026, 6, 1)) };
        rows.AddRange(AugustDays.Select(d => At("r" + d, new DateOnly(2026, 8, d))));
        rows.Add(At("test", new DateOnly(2026, 9, 2)));

        var set = CalendarFolds.Cut(rows, Calendar(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 30)), Monthly(5));

        // The five sessions before 2026-09-01 are 08-24, 25, 26, 28, 31.
        set.Folds.Single(f => f.Start == new DateOnly(2026, 9, 1)).Train.Select(r => r.Key).ShouldBe(["old", "r20", "r21"]);
    }

    [Fact]
    public void The_purge_drops_a_train_row_whose_label_horizon_reaches_the_first_decision_of_the_window()
    {
        var window = new DateOnly(2026, 9, 1);
        var first = Epoch(new DateOnly(2026, 9, 1));
        var rows = new List<ValidationRow>
        {
            At("old", new DateOnly(2026, 6, 1)),
            At("reaches", new DateOnly(2026, 6, 2)) with { ResolvedEpochSeconds = first - 60 },
            At("clear", new DateOnly(2026, 6, 3)) with { ResolvedEpochSeconds = first - 61 },
            At("test", window),
        };

        var set = CalendarFolds.Cut(rows, Calendar(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 30)), Monthly(0));

        set.Folds.Single(f => f.Start == window).Train.Select(r => r.Key).ShouldBe(["old", "clear"]);
    }

    [Fact]
    public void A_last_window_that_the_session_list_does_not_reach_is_dropped_and_counted()
    {
        var rows = new[] { At("a", new DateOnly(2026, 6, 1)), At("b", new DateOnly(2026, 8, 3)), At("c", new DateOnly(2026, 9, 3)) };

        var set = CalendarFolds.Cut(rows, Calendar(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 20)), Monthly());

        set.Folds.Select(f => f.Start).ShouldBe([new DateOnly(2026, 8, 1)]);
        set.IncompleteDropped.ShouldBe(1);
    }

    [Fact]
    public void A_fold_with_too_few_train_rows_or_no_rows_is_skipped_with_the_reason()
    {
        var rows = new[] { At("a", new DateOnly(2026, 6, 1)), At("c", new DateOnly(2026, 9, 3)) };
        var options = Monthly() with { MinTrainTrades = 5 };

        var set = CalendarFolds.Cut(rows, Calendar(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 30)), options);

        set.Folds.Select(f => f.Usable).ShouldBe([false, false]);
        set.Folds[0].SkipReason.ShouldBe("no rows in the window");
        set.Folds[1].SkipReason.ShouldBe("1 train rows, 5 needed");
    }

    [Fact]
    public void Quarterly_windows_are_calendar_quarters_and_the_first_starts_a_year_after_the_first_row_at_the_next_quarter()
    {
        var o = ExtensionComparisonOptions.ForFilings() with { RecipeOptionsHash = "r", LabelRuleVersion = "tb-1", MinTrainTrades = 1 };
        var rows = new[] { At("a", new DateOnly(2025, 2, 10)), At("b", new DateOnly(2026, 4, 2)), At("c", new DateOnly(2026, 8, 20)), At("d", new DateOnly(2026, 11, 3)) };

        var set = CalendarFolds.Cut(rows, Calendar(new DateOnly(2025, 2, 1), new DateOnly(2026, 12, 31)), o);

        // 2025-02-10 + 12 months = 2026-02-10; the next quarter start is 2026-04-01.
        set.Folds.Select(f => (f.Start, f.End)).ShouldBe(
        [
            (new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)),
            (new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)),
            (new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)),
        ]);
        set.Folds[2].Test.Select(r => r.Key).ShouldBe(["d"]);
    }

    [Fact]
    public void A_quarter_boundary_date_stays_and_a_date_inside_a_quarter_moves_to_the_next()
    {
        CalendarFolds.Boundary(new DateOnly(2026, 7, 1), FoldUnit.Quarter).ShouldBe(new DateOnly(2026, 7, 1));
        CalendarFolds.Boundary(new DateOnly(2026, 7, 2), FoldUnit.Quarter).ShouldBe(new DateOnly(2026, 10, 1));
        CalendarFolds.Boundary(new DateOnly(2026, 12, 20), FoldUnit.Quarter).ShouldBe(new DateOnly(2027, 1, 1));
        CalendarFolds.Boundary(new DateOnly(2026, 12, 20), FoldUnit.Month).ShouldBe(new DateOnly(2027, 1, 1));
    }
}
