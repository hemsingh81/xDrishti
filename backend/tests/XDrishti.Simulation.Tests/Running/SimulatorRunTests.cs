using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 10 of P3-03a: <c>Run</c> composes the stages. The trades through <c>Simulator.Run</c> are the trades of the walker table field for field, the candidates are the hand-made ones, nothing is left in a simulator
/// between runs, and four threads sharing one simulator give the results of one.
/// </summary>
public sealed class SimulatorRunTests
{
    private static readonly Simulator Orb = Sim.Create(OrbV1.Create());

    /// <summary>The days of P3-03 that have a candidate: the scanner must make exactly the candidates its tables walk.</summary>
    public static TheoryData<string> Days => [.. Cases.FDays.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Days))]
    public void A_day_gives_the_candidates_and_the_trades_of_the_walker_table_field_for_field(string name)
    {
        var walkCase = Cases.F(name);

        var result = Sim.Run(Orb, walkCase.Bars, Cand.Test);

        result.Outcomes.Count.ShouldBe(walkCase.Items.Count, name);
        for (var i = 0; i < walkCase.Items.Count; i++)
        {
            var item = walkCase.Items[i];
            (result.Outcomes[i].Candidate with { Trace = DecisionTrace.Empty }).ShouldBe(item.Candidate, name);
            WalkAssert.Matches(walkCase, item, Result.Success(result.Outcomes[i].Walk));
        }
    }

    [Fact]
    public void F1_gives_one_trade_and_the_candidate_that_made_it()
    {
        var result = Sim.Run(Orb, Cases.F("F1").Bars, Cand.Test);

        result.Trades.Count.ShouldBe(1);
        result.OpenAtEnd.ShouldBeEmpty();
        result.Outcomes.Single().Walk.Trade.ShouldBeSameAs(result.Trades[0]);
        result.Trades[0].Key.ShouldBe("ORB|TEST|Buy|2026-03-02");
        result.Outcomes.Single().Walk.BarsExamined.ShouldBe(86);
    }

    [Fact]
    public void F6_ends_with_the_data_so_it_is_open_at_the_end_and_not_a_trade()
    {
        var result = Sim.Run(Orb, Cases.F("F6").Bars, Cand.Test);

        result.Trades.ShouldBeEmpty();
        result.OpenAtEnd.Single().ExitReason.ShouldBe(ExitReason.EndOfData);
        result.Outcomes.Count.ShouldBe(1);
        result.Walk.ExitsEndOfData.ShouldBe(1);
    }

    [Fact]
    public void F5_visits_each_minute_of_its_trade_once()
    {
        var result = Sim.Run(Orb, Cases.F("F5").Bars, Cand.Test);

        result.Outcomes.Single().Walk.BarsExamined.ShouldBe(331);
    }

    [Fact]
    public void F13_gives_two_trades_ordered_by_entry_instant_then_buy_before_sell()
    {
        var result = Sim.Run(Orb, Cases.F("F13").Bars, Cand.Test);

        result.Trades.Select(t => (t.Side, t.EntryEpochSeconds)).ShouldBe(
            [(Side.Buy, Day.Epoch(Day.Monday, Day.Minute("09:45"))), (Side.Sell, Day.Epoch(Day.Monday, Day.Minute("10:15")))]);
        result.Scans.Select(s => s.Side).ShouldBe([Side.Buy, Side.Sell]);
    }

    [Fact]
    public void The_trades_are_ordered_by_entry_instant_even_when_the_sell_comes_first()
    {
        // 09:44 closes under the low of the opening range (a SELL at 09:45) and 10:14 closes back over its high (a BUY at 10:15)
        var bars = Day.P().Set("09:44 99.00/99.00/97.50/97.50", "10:14 97.50/100.50/97.50/100.50").Bars();

        var result = Sim.Run(Orb, bars);

        result.Outcomes.Select(o => o.Candidate.Side).ShouldBe([Side.Sell, Side.Buy]);
        result.Trades.Select(t => t.Side).ShouldBe([Side.Sell, Side.Buy]);
        result.Trades[0].EntryEpochSeconds.ShouldBeLessThan(result.Trades[1].EntryEpochSeconds);
    }

    [Fact]
    public void F14_gives_a_candidate_with_no_trade_in_the_outcomes()
    {
        var result = Sim.Run(Orb, Cases.F("F14").Bars, Cand.Test);

        result.Trades.ShouldBeEmpty();
        result.OpenAtEnd.ShouldBeEmpty();
        var outcome = result.Outcomes.Single();
        outcome.Walk.NoTrade.ShouldBe(NoTradeReason.EntryThroughStop);
        outcome.Walk.Trade.ShouldBeNull();
        result.Walk.EntryThroughStop.ShouldBe(1);
    }

    [Theory]
    [InlineData("F8")]
    [InlineData("F10")]
    public void A_day_without_a_candidate_gives_an_empty_result_with_the_counters_of_both_sides(string name)
    {
        var bars = Fixtures.OrbDays.Single(f => f.Name == name).Bars;

        var result = Sim.Run(Orb, bars);

        result.Outcomes.ShouldBeEmpty();
        result.Walk.ShouldBe(WalkCounters.Empty);
        result.Scans.Select(s => (s.Side, s.Bars, s.Candidates)).ShouldBe([(Side.Buy, 375, 0), (Side.Sell, 375, 0)]);
    }

    [Fact]
    public void The_scans_are_the_counters_of_the_scanners_of_each_side()
    {
        var bars = Fixtures.F13().Bars();

        var result = Sim.Run(Orb, bars);

        result.Scans.ShouldBe([.. Scan.Both(OrbV1.Create(), bars).Select(s => s.Counters)]);
    }

    [Fact]
    public void The_walkers_counters_are_the_sums_of_those_of_the_outcomes()
    {
        // Monday: F5, a trade that leaves at the square-off; Tuesday: F14, a fill through the stop
        var days = Day.Join(Fixtures.F5(), Day.P(new DateOnly(2026, 3, 3)).B().Set("09:45 97.75/98.50/97.75/98.00"));

        var result = Sim.Run(Orb, days);

        var sum = result.Outcomes.Aggregate(WalkCounters.Empty, (total, o) => total.Add(o.Walk.ToCounters()));
        result.Outcomes.Count.ShouldBe(2);
        result.Walk.ShouldBe(sum);
        (result.Walk.Trades, result.Walk.ExitsSquareOff, result.Walk.EntryThroughStop).ShouldBe((1, 1, 1));
    }

    [Fact]
    public void The_header_says_what_the_result_is_with_the_five_labels_in_order()
    {
        var result = Sim.Run(Orb, Cases.F("F1").Bars, Cand.Test);

        result.Header.EngineVersion.ShouldBe(SimulationEngine.Version);
        result.Header.SchemaVersion.ShouldBe(SimTrade.CurrentSchemaVersion);
        result.Header.StrategyId.ShouldBe("ORB");
        result.Header.Fills.ShouldBe(FillBasis.Exact);
        result.Header.Costs.ShouldBe(CostBasis.None);
        result.Header.Labels.ShouldBe(["fills: exact", "entry limit: not applied", "costs: none", "portfolio limits: not simulated", "sample: in-sample"]);
    }

    [Fact]
    public void Every_trade_carries_the_instrument_it_was_run_for()
    {
        var instrument = new InstrumentRef(new Guid("00000000-0000-0000-0000-0000000000AB"), "ABC");

        var result = Sim.Run(Orb, Cases.F("F1").Bars, instrument);

        result.Trades[0].InstrumentId.ShouldBe(instrument.Id);
        result.Trades[0].Symbol.ShouldBe("ABC");
        result.Outcomes[0].Candidate.Instrument.ShouldBe(instrument);
    }

    [Fact]
    public void An_empty_list_and_one_minute_give_empty_results()
    {
        var empty = Sim.Run(Orb, []);
        var one = Sim.Run(Orb, Day.P().Bars().Take(1).ToArray());

        empty.Outcomes.ShouldBeEmpty();
        empty.Scans.ShouldAllBe(s => s.Bars == 0);
        one.Outcomes.ShouldBeEmpty();
        one.Scans.ShouldAllBe(s => s.Bars == 1);
    }

    [Fact]
    public void The_calendar_reaches_the_frames_of_both_scanners()
    {
        var special = new TradingCalendar([TradingDay.Special(Day.Monday, 9 * 60 + 15, 12 * 60 + 20, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);
        var bars = Day.P().B().Set("10:05 100.50/100.50/99.50/100.50").EndsAfter("12:19").Bars();

        var with = Sim.Run(Orb, bars, calendar: special);
        var without = Sim.Run(Orb, bars);

        with.Scans.ShouldAllBe(s => s.DecisionSteps == 13 && s.WindowSteps == 9 && s.OutOfWindowSteps == 4);
        without.Scans.ShouldAllBe(s => s.DecisionSteps == 12 && s.WindowSteps == 9 && s.OutOfWindowSteps == 3);
    }

    [Fact]
    public void A_result_of_a_run_never_counts_an_ignored_bar()
    {
        var results = Fixtures.OrbDays.Select(f => Sim.Run(Orb, f.Bars)).ToList();

        results.SelectMany(r => r.Scans).ShouldAllBe(s => s.IgnoredBars == 0);
    }

    [Fact]
    public void A_run_after_another_equals_a_run_on_a_fresh_simulator()
    {
        var simulator = Sim.Create(OrbV1.Create());
        Sim.Run(simulator, Cases.F("F1").Bars);

        var second = Sim.Run(simulator, Cases.F("F13").Bars);

        Render.Result(second).ShouldBe(Render.Result(Sim.Run(Sim.Create(OrbV1.Create()), Cases.F("F13").Bars)));
    }

    [Fact]
    public async Task Four_threads_sharing_one_simulator_give_the_results_of_one_thread()
    {
        var days = new[] { "F1", "F2", "F5", "F13" };
        var instruments = days.Select((d, i) => new InstrumentRef(new Guid(i + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), "T" + i)).ToArray();
        var expected = days.Select((d, i) => Render.Result(Sim.Run(Orb, Cases.F(d).Bars, instruments[i]))).ToArray();

        var results = await Task.WhenAll(days.Select((d, i) => Task.Run(() =>
        {
            var last = string.Empty;
            for (var round = 0; round < 20; round++)
            {
                last = Render.Result(Sim.Run(Orb, Cases.F(d).Bars, instruments[i]));
                last.ShouldBe(expected[i]);
            }

            return last;
        })));

        results.ShouldBe(expected);
    }

    [Fact]
    public void The_sides_the_simulator_runs_are_those_the_strategy_lists_and_buy_comes_first()
    {
        Orb.Sides.ShouldBe([Side.Buy, Side.Sell]);
        Sim.Create(TestStrategies.OrbBuyOnly()).Sides.ShouldBe([Side.Buy]);
    }

    [Fact]
    public void A_null_list_of_bars_or_definition_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => Orb.Run(Scan.Instrument, null!));
        Should.Throw<ArgumentNullException>(() => Simulator.Create(null!, Timeframe.M15));
    }
}
