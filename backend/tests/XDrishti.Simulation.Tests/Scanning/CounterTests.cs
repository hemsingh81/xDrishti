using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criteria 4 to 8 of P3-03a: the counters of every fixture are those of the tables, and the identities of D2 and D10 hold on all of them.</summary>
public sealed class CounterTests
{
    private static EntryScanner Buy(Fixture f) => Scan.Both(f.Definition, f.Bars, f.Timeframe)[0];

    private static ScanCounters BuyCounters(string name) => Buy(Fixtures.All.Single(f => f.Name == name)).Counters;

    [Fact]
    public void The_identities_hold_on_every_fixture_for_every_side()
    {
        foreach (var fixture in Fixtures.All)
        {
            foreach (var scanner in Scan.Both(fixture.Definition, fixture.Bars, fixture.Timeframe))
            {
                Identities.ShouldHold(scanner);
            }
        }
    }

    [Fact]
    public void The_dense_day_f1_gives_the_counters_of_the_spec()
    {
        var buy = Scan.Both(OrbV1.Create(), Fixtures.F1().Bars());

        var c = buy[0].Counters;
        (c.Bars, c.DecisionSteps, c.StaleBucketSteps, c.OutOfWindowSteps, c.WindowSteps, c.FullyReadySteps, c.Candidates, c.LaterTriggers, c.LostDecisions, c.IgnoredBars, c.IncompleteBarsConsumed)
            .ShouldBe((375, 25, 0, 16, 9, 9, 1, 0, 0, 0, 0));
        Identities.Of(c, "buy.trigger[0]").ShouldBe((1, 8, 0, 0));
        Identities.Of(buy[1].Counters, "sell.trigger[0]").ShouldBe((0, 9, 0, 0));
    }

    [Fact]
    public void Thirty_minutes_make_the_first_bar_not_ready_and_the_first_break_the_sixth_bar()
    {
        var scanner = Scan.Run(OrbV1.Create(orMinutes: 30), Side.Buy, Fixtures.F1().Bars());

        var c = scanner.Counters;
        Identities.Of(c, "buy.trigger[0]").ShouldBe((1, 7, 0, 1));
        c.FullyReadySteps.ShouldBe(8);
        Render.Candidate(scanner.Candidates.Single(), withTrace: false)
            .ShouldBe(Expect.Candidate(Side.Buy, 89, "10:45", 98.00, "10:30", 100.50, 104.50, 100.50, 104.00, 15, true));
        Identities.ShouldHold(scanner);
    }

    [Fact]
    public void The_same_minutes_on_5m_give_the_same_decision_instant_and_a_5_minute_trigger_bar()
    {
        var scanner = Scan.Run(OrbV1.Create(), Side.Buy, Fixtures.F1().Bars(), Timeframe.M5);

        Render.Candidate(scanner.Candidates.Single(), withTrace: false)
            .ShouldBe(Expect.Candidate(Side.Buy, 29, "09:45", 98.00, "09:40", 99.00, 100.50, 99.00, 100.50, 5, true, timeframe: Timeframe.M5));
        var c = scanner.Counters;
        (c.DecisionSteps, c.WindowSteps, c.OutOfWindowSteps).ShouldBe((75, 25, 50));
        Identities.Of(c, "buy.trigger[0]").ShouldBe((1, 24, 0, 0));
        Identities.ShouldHold(scanner);
    }

    [Fact]
    public void A_close_equal_to_the_level_is_not_a_break_f8()
    {
        var c = BuyCounters("F8");

        Identities.Of(c, "buy.trigger[0]").ShouldBe((0, 9, 0, 0));
        c.Candidates.ShouldBe(0);
    }

    [Fact]
    public void A_trigger_bar_that_closes_after_the_last_entry_is_counted_and_not_evaluated_f10()
    {
        var c = BuyCounters("F10");

        (c.WindowSteps, c.OutOfWindowSteps, c.Candidates).ShouldBe((9, 16, 0));
    }

    [Fact]
    public void A_second_break_is_a_later_trigger_and_makes_no_second_candidate_f11()
    {
        var c = BuyCounters("F11");

        Identities.Of(c, "buy.trigger[0]").ShouldBe((2, 7, 0, 0));
        (c.Candidates, c.LaterTriggers).ShouldBe((1, 1));
    }

    [Fact]
    public void A_stale_bucket_is_counted_and_not_evaluated_f18()
    {
        var c = BuyCounters("F18");

        (c.Bars, c.DecisionSteps, c.StaleBucketSteps, c.OutOfWindowSteps, c.WindowSteps, c.IncompleteBarsConsumed).ShouldBe((389, 26, 1, 15, 10, 1));
    }

    [Fact]
    public void The_stale_step_is_observable_f18b_and_a_scanner_without_the_rule_would_have_decided_on_yesterdays_bar()
    {
        var fixture = Fixtures.All.Single(f => f.Name == "F18b");
        var scanner = Scan.Both(fixture.Definition, fixture.Bars)[0];

        scanner.Candidates.Select(c => c.DecisionBarIndex).ShouldBe([14, 388]);
        scanner.Candidates.ShouldNotContain(c => c.DecisionBarIndex == 374);
        var c = scanner.Counters;
        (c.Bars, c.DecisionSteps, c.StaleBucketSteps, c.OutOfWindowSteps, c.WindowSteps, c.Candidates, c.LaterTriggers).ShouldBe((389, 26, 1, 0, 25, 2, 23));
        Identities.Of(c, "buy.trigger[0]").ShouldBe((25, 0, 0, 0));
    }

    [Fact]
    public void A_late_bucket_on_the_same_date_is_decided_at_the_minute_that_closed_it_f15()
    {
        var scanner = Scan.Both(OrbV1.Create(), Fixtures.F15().Bars())[0];

        scanner.Candidates.Single().TriggerBarComplete.ShouldBeFalse();
        scanner.Counters.IncompleteBarsConsumed.ShouldBe(1);
        Identities.ShouldHold(scanner);
    }

    [Fact]
    public void A_step_that_closes_two_bars_decides_the_second_and_loses_the_first_f16()
    {
        var scanner = Scan.Both(OrbV1.Create(), Fixtures.F16().Bars())[0];

        var c = scanner.Counters;
        (c.LostDecisions, c.IncompleteBarsConsumed).ShouldBe((1, 2));
        scanner.Candidates.Single().DecisionBarIndex.ShouldBe(14);
        Identities.ShouldHold(scanner);
    }

    [Fact]
    public void A_stream_that_joins_late_has_no_opening_range_and_every_window_step_is_not_ready_on_both_sides()
    {
        var late = Day.Dense(first: "10:00").Start(99.00).Set("10:44 99.00/105.00/99.00/105.00").Bars();

        foreach (var scanner in Scan.Both(OrbV1.Create(), late))
        {
            scanner.Candidates.ShouldBeEmpty();
            var c = scanner.Counters;
            var path = scanner.Counters.Side == Side.Buy ? "buy.trigger[0]" : "sell.trigger[0]";
            Identities.Of(c, path).NotReady.ShouldBe(c.WindowSteps);
            Identities.ShouldHold(scanner);
        }
    }

    [Fact]
    public void The_first_evaluation_of_a_session_is_unknown_not_a_crossing_against_yesterday_f21()
    {
        var fixture = Fixtures.All.Single(f => f.Name == "F21");
        var scanner = Scan.Both(fixture.Definition, fixture.Bars)[0];

        Render.Candidate(scanner.Candidates.Single(), withTrace: false)
            .ShouldBe(Expect.Candidate(Side.Buy, 74, "09:45", 99.00, "09:30", 99.00, 101.00, 99.00, 101.00, 15, true, id: "CROSS_VWAP_TEST", date: new DateOnly(2026, 3, 5)));
        Identities.Of(scanner.Counters, "buy.trigger[0]").ShouldBe((1, 1, 3, 0));
        Identities.ShouldHold(scanner);
    }

    [Fact]
    public void A_positional_reading_would_have_crossed_on_the_first_bar_of_day_b_f21()
    {
        // plain arithmetic: day A's last 15m close and last vwap are 99.00 and 99.00; day B's first bar closes at 101.00 against a vwap of (99.00 x 14 + (101 + 99 + 101) / 3 ... ) / 15
        var vwapOfBarOne = (14 * 99.0 + (101.0 + 99.0 + 101.0) / 3) / 15;
        var previousClose = 99.0;
        var previousVwap = 99.0;
        var close = 101.0;

        (close > vwapOfBarOne && previousClose <= previousVwap).ShouldBeTrue("a positional lag would have said true");
        Identities.Of(Scan.Both(TestStrategies.CrossVwap(), Fixtures.F21())[0].Counters, "buy.trigger[0]").Unknown.ShouldBe(3, "the session-only history says unknown");
    }
}
