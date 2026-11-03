using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.PairResearch;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Pairs;

/// <summary>
/// P14-13 criterion 3 (the planted excursion: one trade entered at the next 1-minute opens of both legs, exited at |z| &lt;= 0.5, R by the hand method, a stop at 3.5, a forced exit at 15:15), the session rules (no entry after 14:45, entry only below the stop), the counted failures and the
/// charge model; criterion 7 (the engine is untouched and the walker reports pair-1).
/// </summary>
public sealed class PairWalkerTests
{
    private static PairWalkResult Walk(Func<int, double> y, Func<int, bool>? absentX = null, int priorSessions = 5, PairVariant? variant = null)
    {
        var result = PairWalker.Walk(PairDays.Input(PairDays.YBars(y, priorSessions), PairDays.XBars(absentX), variant));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private static int Minute(long epoch) => SessionClock.MinuteOfDay(epoch);

    [Fact]
    public void The_planted_excursion_to_z_22_and_back_is_one_short_spread_trade_entered_and_exited_at_the_next_opens()
    {
        var walk = Walk(PairDays.Excursion(20, 100.90, 22, 100.15));

        var trade = walk.Trades.ShouldHaveSingleItem();
        trade.Side.ShouldBe(PairSide.ShortSpread);
        trade.PairKey.ShouldBe("NSE:Y|NSE:X");
        trade.ZEntry.ShouldBeGreaterThan(2.0);
        trade.ZEntry.ShouldBeLessThan(3.5);
        trade.ZExit!.Value.ShouldBeInRange(-0.5, 0.5);
        trade.ExitReason.ShouldBe("exit");
        Minute(trade.EntryEpochSeconds).ShouldBe(660);
        Minute(trade.ExitEpochSeconds).ShouldBe(670);
        (trade.EntryY, trade.ExitY, trade.EntryX, trade.ExitX).ShouldBe((100.90m, 100.15m, 100.00m, 100.00m));
        (trade.QtyY, trade.QtyX).ShouldBe((991, 1000));
        walk.Counters.ShouldBe(PairWalkCounters.Zero);
    }

    [Fact]
    public void The_gross_rupees_are_exact_and_R_is_the_rupees_over_the_risk_of_the_hand_method()
    {
        var trade = Walk(PairDays.Excursion(20, 100.90, 22, 100.15)).Trades.Single();
        var (_, sd) = PairDays.Window();

        trade.GrossInr.ShouldBe(743.25m);
        trade.NetInr.ShouldBeNull();
        var risk = PairTradeRule.RupeeRisk(100_000m, trade.ZEntry, sd, 3.5)!.Value;
        trade.RiskInr.ShouldBe(risk);
        trade.R.ShouldBe((double)(743.25m / risk), 1e-12);
        trade.TurnoverInr.ShouldBe(((100.90m + 100.15m) * 991) + ((100m + 100m) * 1000));
    }

    [Fact]
    public void A_stop_at_z_35_closes_both_legs_at_the_next_opens_with_the_reason_stop()
    {
        var trade = Walk(PairDays.Excursion(20, 100.90, 22, 101.50)).Trades.Single();

        trade.ExitReason.ShouldBe("stop");
        Minute(trade.ExitEpochSeconds).ShouldBe(670);
        trade.ExitY.ShouldBe(101.50m);
        trade.GrossInr.ShouldBe(-594.60m);
        trade.R.ShouldBeLessThan(0);
        Math.Abs(trade.ZExit!.Value).ShouldBeGreaterThanOrEqualTo(3.5);
    }

    [Fact]
    public void A_pair_still_open_at_the_1515_decision_is_closed_flat_with_the_reason_square_off()
    {
        var trade = Walk(PairDays.Excursion(20, 100.90, 80, 100.90)).Trades.Single();

        trade.ExitReason.ShouldBe("square_off");
        Minute(trade.ExitEpochSeconds).ShouldBe(915);
        trade.ExitY.ShouldBe(100.90m);
        trade.GrossInr.ShouldBe(0m);
        trade.R.ShouldBe(0.0);
    }

    [Fact]
    public void A_first_z_at_or_beyond_the_stop_never_opens_and_is_counted()
    {
        var walk = Walk(PairDays.Excursion(20, 101.50, 80, 101.50));

        walk.Trades.ShouldBeEmpty();
        walk.Counters.BeyondStop.ShouldBe(46);
    }

    [Fact]
    public void A_decision_after_1445_does_not_open_and_one_at_1445_does()
    {
        var late = Walk(PairDays.Excursion(66, 100.90, 80, 100.90));
        var last = Walk(PairDays.Excursion(65, 100.90, 80, 100.90));

        late.Trades.ShouldBeEmpty();
        var trade = last.Trades.ShouldHaveSingleItem();
        Minute(trade.EntryEpochSeconds).ShouldBe(885);
        trade.ExitReason.ShouldBe("square_off");
    }

    [Fact]
    public void The_long_spread_side_buys_y_and_sells_x_when_z_is_below_minus_entry()
    {
        var trade = Walk(PairDays.Excursion(20, 99.10, 22, 99.85)).Trades.Single();

        trade.Side.ShouldBe(PairSide.LongSpread);
        trade.ZEntry.ShouldBeLessThan(-2.0);
        trade.EntryY.ShouldBe(99.10m);
        trade.ExitY.ShouldBe(99.85m);
        trade.GrossInr.ShouldBe((99.85m - 99.10m) * trade.QtyY);
    }

    [Fact]
    public void An_entry_with_a_leg_that_has_no_bar_within_the_delay_is_not_made_and_is_counted_and_a_bucket_with_no_close_is_an_unavailable_z()
    {
        var walk = Walk(PairDays.Excursion(20, 100.90, 80, 100.90), absentX: minute => minute is >= 660 and <= 690);

        walk.Counters.LegUnfilled.ShouldBe(1);
        walk.Counters.ZUnavailable.ShouldBe(6);
        var trade = walk.Trades.ShouldHaveSingleItem();
        Minute(trade.EntryEpochSeconds).ShouldBe(695);
    }

    [Fact]
    public void An_exit_with_a_missing_leg_bar_uses_that_legs_next_bar_and_counts_it_late()
    {
        var walk = Walk(PairDays.Excursion(20, 100.90, 22, 100.15), absentX: minute => minute == 670);

        var trade = walk.Trades.Single();
        walk.Counters.LegLate.ShouldBe(1);
        trade.Labels.ShouldContain("leg_late");
        Minute(trade.ExitEpochSeconds).ShouldBe(671);
    }

    [Fact]
    public void Without_a_full_look_back_no_z_exists_so_nothing_opens_and_every_decision_is_counted()
    {
        var walk = Walk(PairDays.Excursion(20, 100.90, 80, 100.90), priorSessions: 3);

        walk.Trades.ShouldBeEmpty();
        walk.Counters.ZUnavailable.ShouldBe(66);
    }

    [Fact]
    public void An_unavailable_z_never_closes_an_open_pair_by_rule_only_the_square_off_does()
    {
        var walk = Walk(PairDays.Excursion(20, 100.90, 80, 100.90), absentX: minute => minute is >= 700 and <= 760);

        var trade = walk.Trades.ShouldHaveSingleItem();
        trade.ExitReason.ShouldBe("square_off");
        walk.Counters.ZUnavailable.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_leg_of_zero_shares_is_not_sizable_and_counted()
    {
        var input = PairDays.Input(PairDays.YBars(PairDays.Excursion(20, 100.90, 22, 100.15)), PairDays.XBars(), options: PairStudyOptions.Default with { PairNotional = 50m });

        var walk = PairWalker.Walk(input).Value;

        walk.Trades.ShouldBeEmpty();
        walk.Counters.NotSizable.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Each_leg_fills_at_the_price_the_single_instrument_walker_fills_it_on_the_same_bar_with_the_default_slippage()
    {
        // A non-flat fill minute so that the range term of the slippage matters; the Y leg sells at 11:00 and the single-instrument walker sells the same bar.
        var y = PairDays.YBars(PairDays.Excursion(20, 100.90, 22, 100.15));
        var fillIndex = y.FindIndex(b => b.EpochSeconds == PairDays.Epoch(PairDays.TradingDay, 660));
        y[fillIndex] = y[fillIndex] with { Open = 100.90, High = 101.10, Low = 100.60, Close = 100.80 };
        var x = PairDays.XBars();
        var xIndex = x.FindIndex(b => b.EpochSeconds == PairDays.Epoch(PairDays.TradingDay, 660));
        x[xIndex] = x[xIndex] with { Open = 100.00, High = 100.30, Low = 99.80, Close = 100.10 };

        var pair = PairWalker.Walk(PairDays.Input(y, x, fills: FillRules.Default)).Value.Trades.Single();

        pair.EntryY.ShouldBe(SingleLegEntry(y, Side.Sell, 660, 100.90));
        pair.EntryX.ShouldBe(SingleLegEntry(x, Side.Buy, 660, 100.00));
    }

    private static decimal SingleLegEntry(List<Bar> bars, Side side, int decisionMinute, double triggerClose)
    {
        var index = bars.FindIndex(b => b.EpochSeconds == PairDays.Epoch(PairDays.TradingDay, decisionMinute)) - 1;
        var trigger = new Bar(PairDays.Epoch(PairDays.TradingDay, decisionMinute - 5), triggerClose, triggerClose, triggerClose, triggerClose, 100);
        var candidate = new EntryCandidate("PAIR", new InstrumentRef(Guid.Empty, "T"), side, index, PairDays.Epoch(PairDays.TradingDay, decisionMinute), Timeframe.M5, trigger, 5, true, side == Side.Buy ? 1.0 : 1000.0, DecisionTrace.Empty);
        var rules = new WalkRules(null, 915, FillRules.Default, PairDays.Tick);

        var outcome = TradeWalker.Walk(candidate, bars, rules).Value;

        return (decimal)outcome.Trade!.EntryFilledPrice;
    }

    [Fact]
    public void Changing_any_bar_after_a_decision_changes_neither_that_decision_nor_the_trade_it_started()
    {
        var baseline = Walk(PairDays.Excursion(20, 100.90, 22, 100.15)).Trades.Single();
        var y = PairDays.YBars(PairDays.Excursion(20, 100.90, 22, 100.15));
        for (var i = 0; i < y.Count; i++)
        {
            if (y[i].EpochSeconds > PairDays.Epoch(PairDays.TradingDay, 700))
            {
                y[i] = y[i] with { Open = 150, High = 160, Low = 140, Close = 155 };
            }
        }

        var changed = PairWalker.Walk(PairDays.Input(y, PairDays.XBars())).Value.Trades;

        changed[0].ShouldBe(baseline with { Labels = changed[0].Labels });
    }

    [Fact]
    public void The_same_bars_give_the_same_trades_and_the_walker_reports_pair_1_beside_the_untouched_engine()
    {
        var one = Walk(PairDays.Excursion(20, 100.90, 22, 100.15));
        var two = Walk(PairDays.Excursion(20, 100.90, 22, 100.15));

        two.Trades.Single().ShouldBe(one.Trades.Single() with { Labels = two.Trades.Single().Labels });
        two.Trades.Single().Labels.ShouldBe(one.Trades.Single().Labels);
        PairWalker.Version.ShouldBe("pair-1");
        SimulationEngine.Version.ShouldBe("sim-9");
    }

    [Fact]
    public void Bars_that_are_not_ascending_and_unusable_options_are_refused_not_thrown()
    {
        var y = PairDays.YBars(PairDays.Excursion(20, 100.90, 22, 100.15));
        (y[10], y[11]) = (y[11], y[10]);

        PairWalker.Walk(PairDays.Input(y, PairDays.XBars())).IsFailure.ShouldBeTrue();
        PairWalker.Walk(PairDays.Input(PairDays.YBars(PairDays.Excursion(20, 100.90, 22, 100.15)), PairDays.XBars(), options: PairStudyOptions.Default with { PairNotional = 0m })).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_wider_entry_level_variant_does_not_open_on_z_22()
    {
        var walk = Walk(PairDays.Excursion(20, 100.90, 22, 100.15), variant: new PairVariant(2.5, 5));

        walk.Trades.ShouldBeEmpty();
    }
}
