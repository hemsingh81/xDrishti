using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>
/// Criterion 4 of P3-03e: the daily evaluation. <c>SWING_DAILY_TEST</c> (1D, BUY, trigger <c>close &gt; 0</c>, stop <c>bar_low</c>) over the days DF1 and DF2: one candidate per dense day, decided at the step of the 15:29 minute with the decision
/// instant 15:30; a daily bar closed late by the first minute of the next date is stale, counted, and never evaluated.
/// </summary>
public sealed class DailyScannerTests
{
    private static Day MondayBar() => Day.Dense(Mon).Start(98.00).Set("09:15 98.00/98.00/98.00/98.00", "10:00 98.00/101.00/97.50/100.00");

    private static Bar[] Df1() => Day.Join(MondayBar(), Day.After(MondayBar(), Tue), Day.After(Day.After(MondayBar(), Tue), Wed));

    [Fact]
    public void DF1_three_dense_days_give_one_candidate_each_decided_at_the_step_of_the_1529_minute_with_the_decision_instant_1530()
    {
        var scanner = Scan.Run(TestStrategies.SwingDaily(), Side.Buy, Df1(), Timeframe.D1);

        var candidates = scanner.Candidates;
        candidates.Select(c => c.DecisionBarIndex).ShouldBe([374, 749, 1124]);
        candidates.Select(c => c.DecisionEpochSeconds).ShouldBe([Day.Epoch(Mon, Day.Minute("15:30")), Day.Epoch(Tue, Day.Minute("15:30")), Day.Epoch(Wed, Day.Minute("15:30"))]);
        candidates.ShouldAllBe(c => c.TriggerTimeframe == Timeframe.D1 && c.TriggerBarMinutes == 375 && c.TriggerBarComplete);
        candidates[0].StopLevel.ShouldBe(97.50, "the stop is the low of the daily bar (bar_low)");
        candidates[0].TriggerBar.Close.ShouldBe(100.00);
        (candidates[0].TriggerBar.Open, candidates[0].TriggerBar.High, candidates[0].TriggerBar.Low).ShouldBe((98.00, 101.00, 97.50));
        var counters = scanner.Counters;
        (counters.DecisionSteps, counters.StaleBucketSteps, counters.Candidates).ShouldBe((3L, 0L, 3L));
        Identities.ShouldHold(scanner);
    }

    [Fact]
    public void DF2_a_missing_1529_minute_makes_the_daily_bar_late_stale_counted_and_never_evaluated()
    {
        var monday = Day.Dense(Mon, "09:15", "15:28").Start(98.00).Set("10:00 98.00/101.00/97.50/100.00");
        var bars = Day.Join(monday, Day.After(monday, Tue));

        var scanner = Scan.Run(TestStrategies.SwingDaily(), Side.Buy, bars, Timeframe.D1);

        var counters = scanner.Counters;
        counters.StaleBucketSteps.ShouldBe(1, "Monday's bar closes at Tuesday's first minute, on an earlier date");
        counters.DecisionSteps.ShouldBe(2);
        counters.IncompleteBarsConsumed.ShouldBe(1);
        scanner.Candidates.ShouldHaveSingleItem().DecisionEpochSeconds.ShouldBe(Day.Epoch(Tue, Day.Minute("15:30")), "no candidate on Monday: no order may be placed on yesterday's bar");
        scanner.Candidates[0].DecisionBarIndex.ShouldBe(748);
        Identities.ShouldHold(scanner);
    }

    [Fact]
    public void A_daily_candidate_trigger_bar_starts_at_the_open_and_the_decision_is_taken_on_the_close_not_before()
    {
        var scanner = Scan.Run(TestStrategies.SwingDaily("close > 100.5"), Side.Buy, Df1(), Timeframe.D1);

        scanner.Candidates.ShouldBeEmpty("Monday closes at 100.00: the condition is false at 15:29");
        scanner.Counters.DecisionSteps.ShouldBe(3);
    }

    [Theory]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void A_weekly_or_monthly_evaluation_is_refused_by_the_simulator_by_name(Timeframe timeframe)
    {
        var build = Simulator.Create(TestStrategies.Swing(timeframe), timeframe);

        build.Simulator.ShouldBeNull();
        build.Problems.ShouldNotBeEmpty();
    }
}
