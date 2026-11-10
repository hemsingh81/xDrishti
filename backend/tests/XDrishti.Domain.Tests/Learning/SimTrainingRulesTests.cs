using System.Collections.Immutable;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>AU-13 (ADR 0243 point 4, ADR 0249): the embargo conversion, the hold-out admission rule, the missing quantity, and the fold boundaries and leakage of the walk-forward folds this path reuses, with hand-made dates.</summary>
public sealed class SimTrainingRulesTests
{
    private static readonly DateOnly HoldOut = new(2025, 7, 1);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 3)]
    [InlineData(5, 7)]
    [InlineData(10, 14)]
    [InlineData(20, 28)]
    public void Sessions_become_calendar_days_rounded_up_and_five_sessions_are_the_seven_days_of_ADR_0067(int sessions, int days) =>
        SimTrainingRules.EmbargoDays(sessions, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), Weekday).ShouldBe(days);

    private static bool Weekday(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    [Fact]
    public void A_holiday_week_makes_the_embargo_longer_than_five_calendar_days_a_week()
    {
        // Monday 2024-03-25 is a holiday: the fifth session before Monday 2024-04-01 is Friday 2024-03-22, ten days back
        var holiday = new DateOnly(2024, 3, 25);
        var plain = SimTrainingRules.EmbargoDays(5, new DateOnly(2024, 3, 1), new DateOnly(2024, 4, 30), Weekday);
        var withHoliday = SimTrainingRules.EmbargoDays(5, new DateOnly(2024, 3, 1), new DateOnly(2024, 4, 30), d => Weekday(d) && d != holiday);

        plain.ShouldBe(7);
        withHoliday.ShouldBe(10);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void An_embargo_outside_zero_to_twenty_sessions_is_refused(int sessions) =>
        Should.Throw<ArgumentOutOfRangeException>(() => SimTrainingRules.EmbargoDays(sessions, new DateOnly(2024, 1, 1), new DateOnly(2024, 2, 1), Weekday));

    [Fact]
    public void A_trade_whose_exit_reaches_the_hold_out_block_is_left_out_whole_and_the_last_day_before_it_is_in()
    {
        var lastBefore = new DateOnly(2025, 6, 30);

        SimTrainingRules.Admits(lastBefore, At(lastBefore, 10), lastBefore, HoldOut).ShouldBeTrue();
        SimTrainingRules.Admits(lastBefore, At(HoldOut, 3), lastBefore, HoldOut).ShouldBeFalse("decided before the block but exits in it: the label would use the block");
        SimTrainingRules.Admits(HoldOut, At(HoldOut, 3), HoldOut.AddDays(-1), HoldOut).ShouldBeFalse("decided on the first day of the block");
        SimTrainingRules.Admits(lastBefore.AddDays(1), At(lastBefore, 1), lastBefore, HoldOut).ShouldBeFalse("decided after the end of the training window");
    }

    [Fact]
    public void The_missing_quantity_is_named_rows_first_then_folds()
    {
        SimTrainingRules.NotEvaluable(40, 1000, 0, 6, null, null).ShouldBe("rows: 40 usable closed trades, 1000 needed");
        SimTrainingRules.NotEvaluable(5000, 1000, 2, 6, new DateOnly(2024, 1, 2), new DateOnly(2024, 12, 20)).ShouldBe("walk-forward folds: 2 usable, 6 needed (the history runs 2024-01-02 to 2024-12-20; the first test fold starts 9 months in)");
        SimTrainingRules.NotEvaluable(5000, 1000, 6, 6, null, null).ShouldBeNull();
    }

    [Fact]
    public void Every_fold_trains_only_on_the_past_with_the_embargo_and_the_purge_and_no_row_is_in_the_hold_out()
    {
        var rows = Rows(new DateOnly(2023, 1, 2), 900, horizonDays: 6).Where(r => SimTrainingRules.Admits(r.TradeDate, r.ResolvedEpochSeconds, new DateOnly(2025, 6, 27), HoldOut)).ToList();
        var recorder = new Recorder();
        var embargoDays = SimTrainingRules.EmbargoDays(5, new DateOnly(2023, 1, 2), new DateOnly(2025, 6, 27), Weekday);

        var report = WalkForwardValidator.Compare(rows, [recorder], SimTrainingRules.Options(7, 1) with { MinTrainTrades = 5 }).Value;

        report.Results.Count.ShouldBeGreaterThanOrEqualTo(4);
        recorder.Folds.Count.ShouldBe(report.Results.Count);
        report.Folds.Where(f => f.Usable).Select(f => f.TestStart).ToList().First().ShouldBeGreaterThanOrEqualTo(new DateOnly(2023, 1, 2).AddMonths(9), "the first test fold is 9 months in");
        foreach (var (fold, train) in recorder.Folds.Zip(report.Folds.Where(f => f.Usable), (t, f) => (f, t)))
        {
            train.ShouldNotBeEmpty();
            train.ShouldAllBe(r => r.TradeDate < fold.TestStart.AddDays(-embargoDays), "the embargo: nothing from the days before the test window");
            var firstDecision = rows.Where(r => r.TradeDate >= fold.TestStart && r.TradeDate <= fold.TestEnd).Min(r => r.DecisionEpochSeconds);
            train.ShouldAllBe(r => r.ResolvedEpochSeconds + 60 < firstDecision, "the purge: no train label resolves after the first test decision");
            train.ShouldAllBe(r => r.TradeDate < HoldOut, "no row of the hold-out block is in any fold");
        }

        rows.ShouldAllBe(r => r.TradeDate < HoldOut);
    }

    [Fact]
    public void A_longer_embargo_removes_more_train_rows_and_a_label_that_reaches_the_test_window_is_purged()
    {
        var rows = Rows(new DateOnly(2023, 1, 2), 900, horizonDays: 6);
        var none = new Recorder();
        var long20 = new Recorder();

        WalkForwardValidator.Compare(rows, [none], SimTrainingRules.Options(0, 1) with { MinTrainTrades = 5 });
        WalkForwardValidator.Compare(rows, [long20], SimTrainingRules.Options(28, 1) with { MinTrainTrades = 5 });

        none.Folds.Count.ShouldBe(long20.Folds.Count);
        none.Folds.Zip(long20.Folds).ShouldAllBe(p => p.Second.Count <= p.First.Count);
        none.Folds.Zip(long20.Folds).ShouldContain(p => p.Second.Count < p.First.Count);
        none.Folds.SelectMany(f => f).ShouldAllBe(r => r.ResolvedEpochSeconds > 0);
    }

    [Fact]
    public void The_hold_out_filter_is_by_the_label_horizon_not_by_the_decision_date()
    {
        var rows = Rows(new DateOnly(2025, 6, 1), 40, horizonDays: 6);
        var kept = rows.Where(r => SimTrainingRules.Admits(r.TradeDate, r.ResolvedEpochSeconds, new DateOnly(2025, 6, 30), HoldOut)).ToList();

        kept.ShouldNotBeEmpty();
        kept.ShouldAllBe(r => SessionClock.DateOf(r.ResolvedEpochSeconds) < HoldOut);
        rows.Count(r => r.TradeDate <= new DateOnly(2025, 6, 30) && SessionClock.DateOf(r.ResolvedEpochSeconds) >= HoldOut).ShouldBeGreaterThan(0, "the fixture has a decision before the block whose label reaches into it");
    }

    private static long At(DateOnly day, int hour) => new DateTimeOffset(day.Year, day.Month, day.Day, hour, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static List<ValidationRow> Rows(DateOnly start, int days, int horizonDays)
    {
        var rows = new List<ValidationRow>();
        for (var i = 0; i < days; i++)
        {
            var day = start.AddDays(i);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var decision = At(day, 5);
            var resolved = At(day.AddDays(horizonDays), 9);
            rows.Add(new ValidationRow($"k{i:D4}", i % 3 == 0 ? 1 : 0, i % 3 == 0 ? 1 : -1, day, decision, resolved, "c", ImmutableArray.Create(i % 7 / 7.0)));
        }

        return rows;
    }

    private sealed class Recorder : IScorerFactory
    {
        public string Name => "recorder";

        public List<IReadOnlyList<ValidationRow>> Folds { get; } = [];

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            Folds.Add(train);
            return new Half();
        }

        private sealed class Half : IRowScorer
        {
            public double Score(ValidationRow row) => 0.5;
        }
    }
}
