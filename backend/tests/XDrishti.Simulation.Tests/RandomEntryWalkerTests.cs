using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// CF-07, criteria 2 and 3 (ADR 0208): the random-entry walk. The same seed gives the same draws and another seed differs; the draws of a trade do not depend on the other trades of the plan; each random trade has the real trade's side, session, target
/// multiple and charge sheet, and enters inside the strategy's entry window; a draw that cannot trade is drawn again and then dropped and counted; no session after the end of the window is read; what cannot be restated says why and has no number.
/// </summary>
public sealed class RandomEntryWalkerTests
{
    private static readonly PreparedStrategy Orb = Rep.Prepare(OrbV1.Create());

    private static readonly DateOnly Monday = Day.Monday;

    private static Bar[] Bars(ulong seed = 11) => SeededSeries.Of(seed);

    private static InstrumentReplayInput Input(IReadOnlyList<Bar> bars, DateOnly? to = null, CostContext? costs = null, SkipReason? preset = null) =>
        Rep.Input(bars, Monday, to ?? SessionClock.DateOf(bars[^1].EpochSeconds), costs: costs ?? TestCostSheet.Context(), preset: preset);

    private static NullTradeSpec Spec(int id = 0, Side side = Side.Buy, double risk = 0.01, double rr = 2, DateOnly? date = null) =>
        new(id, Rep.Id, date ?? Monday, side, risk, rr, CostSegment.IntradayEquity);

    private static NullWalkPlan Plan(int draws = 20, int redraws = 5, ulong seed = 7, params NullTradeSpec[] trades) => new(trades.Length == 0 ? [Spec()] : trades, draws, redraws, seed);

    private static NullWalkResult Walk(PreparedStrategy strategy, InstrumentReplayInput input, NullWalkPlan plan, IProgress<int>? done = null, CancellationToken? token = null) =>
        RandomEntryWalker.Walk(strategy, input, plan, done, token ?? TestContext.Current.CancellationToken);

    [Fact]
    public void The_same_seed_gives_the_same_draws_and_another_seed_differs()
    {
        var input = Input(Bars());

        var one = Walk(Orb, input, Plan(seed: 7)).Trades.Single();
        var again = Walk(Orb, input, Plan(seed: 7)).Trades.Single();
        var other = Walk(Orb, input, Plan(seed: 8)).Trades.Single();

        one.Unavailable.ShouldBeNull();
        one.NetR.Count.ShouldBe(20);
        one.NetR.ShouldBe(again.NetR);
        one.NetR.ShouldNotBe(other.NetR);
        one.NetR.Distinct().Count().ShouldBeGreaterThan(1, "the entry minute moves");
    }

    [Fact]
    public void A_strategy_that_names_min_turnover_has_every_draw_blocked_without_the_lookup_and_none_with_enough_turnover()
    {
        var named = Rep.Prepare(OrbV1.Create(), fills: FillRules.Create(0.1, SlippageConfig.Default, 0, false, 3m, 8m, 20m).Value);
        var input = Input(Bars());
        var spec = Spec();

        var blocked = RandomEntryWalker.RandomTrades(named, input, Plan(draws: 10, trades: spec), spec);
        var thin = RandomEntryWalker.RandomTrades(named, input with { TurnoverOn = _ => 1m }, Plan(draws: 10, trades: spec), spec);
        var passed = RandomEntryWalker.RandomTrades(named, input with { TurnoverOn = _ => 100m * LiquidityTiers.Crore }, Plan(draws: 10, trades: spec), spec);

        blocked.Trades.OfType<SimTrade>().ShouldBeEmpty("an unknown turnover is never a pass");
        thin.Trades.OfType<SimTrade>().ShouldBeEmpty();
        passed.Trades.OfType<SimTrade>().ShouldNotBeEmpty();
    }

    [Fact]
    public void BK_11b_the_member_filter_is_applied_to_a_draw_and_a_constant_membership_changes_nothing()
    {
        var input = Input(Bars());
        var spec = Spec();

        var plain = RandomEntryWalker.RandomTrades(Orb, input, Plan(draws: 10, trades: spec), spec);
        var member = RandomEntryWalker.RandomTrades(Orb, input with { MemberOn = _ => true }, Plan(draws: 10, trades: spec), spec);
        var leaver = RandomEntryWalker.RandomTrades(Orb, input with { MemberOn = _ => false }, Plan(draws: 10, trades: spec), spec);

        member.Trades.OfType<SimTrade>().Select(t => t.Key).ShouldBe(plain.Trades.OfType<SimTrade>().Select(t => t.Key));
        leaver.Trades.OfType<SimTrade>().ShouldBeEmpty("a draw on a day the instrument was not a member is no trade");
    }

    [Fact]
    public void The_draws_of_a_trade_do_not_depend_on_the_other_trades_of_the_plan()
    {
        var input = Input(Bars());
        var alone = Walk(Orb, input, Plan(trades: [Spec(1, Side.Sell, 0.015)])).Trades.Single();

        var together = Walk(Orb, input, Plan(trades: [Spec(0), Spec(1, Side.Sell, 0.015), Spec(2, rr: 3)])).Trades.Single(t => t.TradeId == 1);

        together.NetR.ShouldBe(alone.NetR);
    }

    [Fact]
    public void Each_random_trade_has_the_real_trades_side_session_target_and_sheet_and_enters_inside_the_window()
    {
        var input = Input(Bars());
        var spec = Spec(side: Side.Sell, risk: 0.01, rr: 2.5);

        var (trades, _, unavailable) = RandomEntryWalker.RandomTrades(Orb, input, Plan(draws: 40, trades: spec), spec);

        unavailable.ShouldBeNull();
        var kept = trades.OfType<SimTrade>().ToList();
        kept.Count.ShouldBeGreaterThan(10);
        foreach (var t in kept)
        {
            t.Side.ShouldBe(Side.Sell);
            t.TradeDate.ShouldBe(spec.Date);
            t.InstrumentId.ShouldBe(spec.InstrumentId);
            Math.Abs(t.TargetPrice!.Value - t.EntryPrice).ShouldBe(2.5 * Math.Abs(t.EntryPrice - t.StopPrice), 1e-6, "the target multiple of the real trade");
            t.Costing!.Segment.ShouldBe(CostSegment.IntradayEquity);
            t.Costing.SheetId.ShouldBe(TestCostSheet.Id);
            var clock = SessionClock.MinuteOfDay(t.DecisionEpochSeconds - 60) + 1;
            clock.ShouldBeInRange(9 * 60 + 30, 11 * 60 + 30);

            // the charges are the sheet's at the random trade's own quantity: costing the gross trade again gives the same net R
            var again = TradeCostApplier.Apply(t with { Costing = null, Costs = CostBasis.None }, input.Costs!, CostSegment.IntradayEquity).Value.Trade!;
            again.Costing!.RNet.ShouldBe(t.Costing.RNet);
            again.Costing.Quantity.ShouldBe(t.Costing.Quantity);
        }
    }

    [Fact]
    public void A_draw_that_cannot_trade_is_drawn_again_up_to_the_cap_and_then_dropped_and_counted()
    {
        // no minute of the entry window holds a trade: every draw is "no fill bar"
        var empty = ReplayDays.Flat(Monday, 100).Select(b => SessionClock.MinuteOfDay(b.EpochSeconds) is >= 9 * 60 + 25 and <= 11 * 60 + 40 ? b with { Volume = 0 } : b).ToArray();
        var input = Input(empty);

        var drawn = Walk(Orb, input, Plan(draws: 10, redraws: 2)).Trades.Single();

        drawn.Unavailable.ShouldBeNull();
        drawn.NetR.ShouldAllBe(v => double.IsNaN(v), "dropped");
        drawn.Redraws.ShouldBe(10 * 2, "each of the ten draws is drawn again twice and then dropped");
    }

    [Fact]
    public void A_session_after_the_end_of_the_window_is_refused_and_the_bars_after_it_are_never_read()
    {
        var bars = Bars(2);
        var firstDay = bars.Where(b => SessionClock.DateOf(b.EpochSeconds) == Monday).ToArray();
        var later = bars.Where(b => SessionClock.DateOf(b.EpochSeconds) > Monday).Select(b => b with { Open = 9_999, High = 9_999, Low = 9_999, Close = 9_999 }).ToArray();
        var cut = Input(firstDay, Monday);
        var withLater = Input([.. firstDay, .. later], Monday);

        var clean = Walk(Orb, cut, Plan()).Trades.Single();
        var scrambled = Walk(Orb, withLater, Plan()).Trades.Single();
        var beyond = Walk(Orb, cut, Plan(trades: Spec(date: Monday.AddDays(1)))).Trades.Single();

        scrambled.NetR.ShouldBe(clean.NetR, "bars dated after the end of the window change nothing");
        beyond.NetR.ShouldBeEmpty();
        beyond.Unavailable.ShouldNotBeNull().ShouldContain("after the end of the window");
    }

    [Fact]
    public void A_trade_that_cannot_be_restated_says_why_and_has_no_numbers()
    {
        var bars = Bars();
        var gross = Walk(Orb, Rep.Input(bars, Monday, Monday, costs: null), Plan()).Trades.Single();
        var skipped = Walk(Orb, Input(bars, preset: SkipReason.CsvAdjustedTwice), Plan()).Trades.Single();
        var noBars = Walk(Orb, Input(bars), Plan(trades: Spec(date: Monday.AddDays(30)))).Trades.Single();
        var swing = Walk(Rep.Prepare(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default), Input(bars), Plan()).Trades.Single();

        gross.Unavailable.ShouldNotBeNull().ShouldContain("gross");
        skipped.Unavailable.ShouldNotBeNull().ShouldContain("csv_adjusted_twice");
        noBars.Unavailable.ShouldNotBeNull();
        swing.Unavailable.ShouldNotBeNull().ShouldContain("swing");
        new[] { gross, skipped, noBars, swing }.ShouldAllBe(t => t.NetR.Count == 0);
    }

    [Fact]
    public void Trades_of_another_instrument_are_not_walked()
    {
        var other = new NullTradeSpec(0, Guid.NewGuid(), Monday, Side.Buy, 0.01, 2, CostSegment.IntradayEquity);

        Walk(Orb, Input(Bars()), Plan(trades: other)).Trades.ShouldBeEmpty();
    }

    [Fact]
    public void The_stop_is_the_risk_fraction_away_from_the_trigger_close_of_the_random_minute()
    {
        var spec = Spec(side: Side.Buy, risk: 0.012);

        var (trades, _, unavailable) = RandomEntryWalker.RandomTrades(Orb, Input(Bars()), Plan(draws: 30, trades: spec), spec);

        unavailable.ShouldBeNull();
        var kept = trades.OfType<SimTrade>().ToList();
        kept.ShouldNotBeEmpty();
        foreach (var t in kept)
        {
            Math.Abs(t.TriggerBar.Close - t.StopPrice).ShouldBe(0.012 * t.TriggerBar.Close, 1e-9, "the fraction is of the price the stop is placed from");
        }
    }

    [Fact]
    public void Each_trade_is_reported_once_when_it_is_done()
    {
        var seen = new List<int>();

        Walk(Orb, Input(Bars()), Plan(draws: 5, trades: [Spec(0), Spec(1), Spec(2)]), new Collect(seen));

        seen.ShouldBe([1, 1, 1]);
    }

    [Fact]
    public void A_cancelled_token_stops_the_walk_before_any_trade_is_walked()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var seen = new List<int>();

        Should.Throw<OperationCanceledException>(() => Walk(Orb, Input(Bars()), Plan(trades: [Spec(0), Spec(1)]), new Collect(seen), cts.Token));

        seen.ShouldBeEmpty();
    }

    [Fact]
    public void A_cancel_during_the_walk_stops_it_at_the_next_trade()
    {
        using var cts = new CancellationTokenSource();
        var seen = new List<int>();

        Should.Throw<OperationCanceledException>(() => Walk(Orb, Input(Bars()), Plan(draws: 5, trades: [Spec(0), Spec(1), Spec(2)]), new Collect(seen, () => cts.Cancel()), cts.Token));

        seen.ShouldBe([1], "the cancel was seen before the second trade");
    }

    private sealed class Collect(List<int> seen, Action? after = null) : IProgress<int>
    {
        public void Report(int value)
        {
            seen.Add(value);
            after?.Invoke();
        }
    }
}
