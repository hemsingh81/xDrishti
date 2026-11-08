using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// CF-05: the conservative slippage tiers by hand (criterion 4: entry price Rs 1,000, tick Rs 0.05, minute range Rs 4.00, 1R = Rs 10) and the fill-one-minute-later rule (criterion 5, the fill side): the same limit, the fill minute the next minute that holds a trade,
/// a miss when the minute does not come back. Nothing here is a verdict.
/// </summary>
public sealed class CostStressFillTests
{
    private const decimal Tick = 0.05m;

    private static readonly SlippageConfig Default = SlippageConfig.Default;

    /// <summary>The tiered preset: 1 tick floor, 2 extra ticks on a stop exit, 10 % of the range (the bps are the tier's, given to the slip).</summary>
    private static readonly SlippageConfig Conservative = SlippageConfig.Create(1, 0.10, 2).Value;

    [Fact]
    public void The_default_rule_costs_0_025_r_on_the_entry_and_0_035_r_on_a_stop_exit_at_rupees_ten_a_share_of_risk()
    {
        var slip = Slip.Of(Default, Tick);

        (slip.Entry(true, 1000m, 1001m, 4m) - 1000m).ShouldBe(0.25m, "0.05 + 0.05 x 4");
        (1000m - slip.Exit(true, ExitReason.Stop, 1000m, 4m)).ShouldBe(0.35m, "(1 + 2) x 0.05 + 0.20");
    }

    [Fact]
    public void Tier_A_costs_0_07_r_on_the_entry_and_0_08_r_on_a_stop_exit_and_a_target_exit_stays_free()
    {
        var slip = Slip.Of(Conservative, Tick, tierBps: 3m);

        (slip.Entry(true, 1000m, 1001m, 4m) - 1000m).ShouldBe(0.70m, "max(0.05, 3 bps x 1,000 = 0.30) + 0.10 x 4");
        (1000m - slip.Exit(true, ExitReason.Stop, 1000m, 4m)).ShouldBe(0.80m, "0.30 + 2 x 0.05 + 0.40");
        (1010m - slip.Exit(true, ExitReason.Target, 1010m, 4m)).ShouldBe(0m);
    }

    [Fact]
    public void A_stop_out_round_trip_costs_0_15_r_in_tier_A_against_0_06_r_by_default()
    {
        var tiered = Slip.Of(Conservative, Tick, 3m);
        var plain = Slip.Of(Default, Tick);

        var tieredRoundTrip = (tiered.Entry(true, 1000m, 1001m, 4m) - 1000m) + (1000m - tiered.Exit(true, ExitReason.Stop, 1000m, 4m));
        var plainRoundTrip = (plain.Entry(true, 1000m, 1001m, 4m) - 1000m) + (1000m - plain.Exit(true, ExitReason.Stop, 1000m, 4m));

        (tieredRoundTrip / 10m).ShouldBe(0.15m);
        (plainRoundTrip / 10m).ShouldBe(0.06m);
    }

    [Fact]
    public void Tier_B_uses_its_eight_bps_and_a_cheap_name_falls_back_to_the_one_tick_floor()
    {
        var b = Slip.Of(Conservative, Tick, 8m);
        var cheap = Slip.Of(Conservative, Tick, 3m);

        (b.Entry(true, 1000m, 1005m, 4m) - 1000m).ShouldBe(1.20m, "max(0.05, 8 bps x 1,000 = 0.80) + 0.40");
        (cheap.Entry(true, 10m, 11m, 0m) - 10m).ShouldBe(0.05m, "3 bps of Rs 10 is Rs 0.003: the tick floor wins");
    }

    [Fact]
    public void The_entry_never_passes_the_limit_and_a_sell_mirrors_a_buy()
    {
        var slip = Slip.Of(Conservative, Tick, 3m);

        slip.Entry(true, 1000m, 1000.30m, 4m).ShouldBe(1000.30m, "0.70 would pass the limit");
        slip.Entry(false, 1000m, 999.60m, 4m).ShouldBe(999.60m);
        slip.Entry(false, 1000m, 990m, 4m).ShouldBe(999.30m);
        slip.Exit(false, ExitReason.Stop, 1000m, 4m).ShouldBe(1000.80m);
    }

    [Fact]
    public void A_slip_without_a_tier_is_exactly_the_default_rule()
    {
        foreach (var range in new[] { 0m, 0.3m, 4m, 17.55m })
        {
            Slip.Of(Default, Tick, null).Entry(true, 1234.55m, 1300m, range).ShouldBe(Slip.Of(Default, Tick).Entry(true, 1234.55m, 1300m, range));
            Slip.Of(Default, Tick, null).Exit(true, ExitReason.Stop, 1234.55m, range).ShouldBe(Slip.Of(Default, Tick).Exit(true, ExitReason.Stop, 1234.55m, range));
        }
    }

    [Fact]
    public void Property_a_tiered_fill_lies_on_the_grid_never_improves_the_price_and_is_never_better_than_the_untiered_one()
    {
        var rng = new Random(20261104);
        var tiered = Slip.Of(Conservative, Tick, 8m);
        var plain = Slip.Of(Conservative, Tick);
        for (var i = 0; i < 2000; i++)
        {
            var price = 50m + (rng.Next(0, 400_000) * Tick);
            var range = rng.Next(0, 400) * Tick;
            var buy = rng.Next(2) == 0;
            var limit = buy ? price * 1.002m : price * 0.998m;
            var reason = (ExitReason)new[] { ExitReason.Stop, ExitReason.SquareOff, ExitReason.Target }[rng.Next(3)];

            var entry = tiered.Entry(buy, price, limit, range);
            var exit = tiered.Exit(buy, reason, price, range);

            if (entry != limit)
            {
                (entry % Tick).ShouldBe(0m, "on the grid unless it is the limit itself");
            }
            (buy ? entry >= price : entry <= price).ShouldBeTrue("an entry never improves");
            (buy ? entry <= Math.Max(limit, price) : entry >= Math.Min(limit, price)).ShouldBeTrue("an entry never passes the limit");
            (buy ? exit <= price : exit >= price).ShouldBeTrue("an exit never improves");
            (exit % Tick).ShouldBe(0m);
            (buy ? exit <= plain.Exit(buy, reason, price, range) : exit >= plain.Exit(buy, reason, price, range)).ShouldBeTrue("the tier only adds");
        }
    }

    private static FillRules Tiered => FillRules.Create(0.1, Conservative, 0, conservativeSlippage: true).Value;

    private static Hand TierHand() => Hand.Buy("tier", 1000.00, 990.00).Rows(Row("10:00", 1000.00, 1004.00, 999.95, 1000.00));

    [Fact]
    public void Conservative_slippage_prices_a_tier_A_entry_at_the_tier_floor_above_the_open()
    {
        var plain = TierHand().Walk(FillRules.Create(0.1, Conservative, 0).Value).Trade.ShouldNotBeNull();
        var tiered = TierHand().Walk(Tiered, _ => LiquidityTier.A).Trade.ShouldNotBeNull();

        plain.EntryFilledPrice.ShouldBe(1000.50, "0.05 + 0.10 x 4.05 = 0.455, up to the grid");
        tiered.EntryFilledPrice.ShouldBe(1000.75, "max(0.05, 3 bps x 1,000 = 0.30) + 0.405 = 0.705, up to the grid");
    }

    [Fact]
    public void Conservative_slippage_excludes_tier_C_unavailable_and_a_missing_lookup_and_counts_them()
    {
        foreach (Func<DateOnly, LiquidityTier>? tierOn in new Func<DateOnly, LiquidityTier>?[] { _ => LiquidityTier.C, _ => LiquidityTier.Unavailable, null })
        {
            var outcome = TierHand().Walk(Tiered, tierOn);

            outcome.Trade.ShouldBeNull();
            outcome.NoTrade.ShouldBe(NoTradeReason.TierExcluded);
            outcome.ToCounters().TierExcluded.ShouldBe(1);
        }
    }

    [Fact]
    public void Without_the_option_the_tier_lookup_is_never_read()
    {
        var read = false;
        var trade = TierHand().Walk(FillRules.Create(0.1, Conservative, 0).Value, _ => { read = true; return LiquidityTier.C; });

        trade.Trade.ShouldNotBeNull();
        read.ShouldBeFalse();
    }

    private static string Row(string time, double open, double high, double low, double close) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{time} {open}/{high}/{low}/{close}");

    private static string Empty(string time) => time + " 100.00/100.00/100.00/100.00 v0";

    private static FillRules Delayed(int minutes) => FillRules.Create(0.1, SlippageConfig.None, minutes).Value;

    private static WalkOutcome Walk(Hand hand, int delay) => hand.Walk(Delayed(delay));

    [Fact]
    public void With_a_delay_of_one_minute_the_entry_fills_at_the_open_of_the_next_minute_that_holds_a_trade()
    {
        var hand = Hand.Buy("delay", 100.00, 99.00).Rows(Row("10:00", 100.00, 100.05, 99.95, 100.00), Row("10:01", 100.05, 100.10, 100.00, 100.05));

        var baseTrade = Walk(hand, 0).Trade.ShouldNotBeNull();
        var late = Walk(hand, 1).Trade.ShouldNotBeNull();

        baseTrade.EntryEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("10:00")));
        baseTrade.EntryPrice.ShouldBe(100.00);
        late.EntryEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("10:01")));
        late.EntryPrice.ShouldBe(100.05);
        late.EntryFill.ShouldBe(EntryFill.AtOpen);
    }

    [Fact]
    public void An_empty_minute_is_passed_over_before_and_after_the_delay()
    {
        var hand = Hand.Buy("empty", 100.00, 99.00).Rows(Empty("10:00"), Row("10:01", 100.00, 100.05, 99.95, 100.00), Empty("10:02"), Row("10:03", 100.05, 100.10, 100.00, 100.05));

        Walk(hand, 0).Trade.ShouldNotBeNull().EntryEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("10:01")));
        Walk(hand, 1).Trade.ShouldNotBeNull().EntryEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("10:03")));
    }

    [Fact]
    public void The_limit_is_unchanged_so_a_runner_that_left_the_limit_behind_is_missed_by_the_delayed_entry()
    {
        // limit 100.10: the base fills at 10:00; the next minute opens at 100.30 and never trades back to the limit (the trades that ran are the missed ones)
        var hand = Hand.Buy("ran", 100.00, 99.00).Rows(Row("10:00", 100.00, 100.05, 99.95, 100.00), Row("10:01", 100.30, 100.60, 100.20, 100.50));

        Walk(hand, 0).Trade.ShouldNotBeNull();
        var late = Walk(hand, 1);
        late.Trade.ShouldBeNull();
        late.NoTrade.ShouldBe(NoTradeReason.LimitNotReached);
    }

    [Fact]
    public void A_delayed_minute_that_opens_beyond_the_limit_and_trades_back_to_it_fills_at_the_limit()
    {
        var hand = Hand.Buy("back", 100.00, 99.00).Rows(Row("10:00", 100.00, 100.05, 99.95, 100.00), Row("10:01", 100.30, 100.40, 100.10, 100.20));

        var late = Walk(hand, 1).Trade.ShouldNotBeNull();

        late.EntryFill.ShouldBe(EntryFill.AtLimit);
        late.EntryPrice.ShouldBe(100.10);
    }

    [Fact]
    public void A_list_that_ends_before_the_delayed_minute_is_no_fill_and_a_delay_of_zero_is_the_old_rule()
    {
        var hand = Hand.Buy("end", 100.00, 99.00).Rows(Row("10:00", 100.00, 100.05, 99.95, 100.00)).EndsAfter("10:00");

        Walk(hand, 0).Trade.ShouldNotBeNull();
        Walk(hand, 1).NoTrade.ShouldBe(NoTradeReason.NoFillBar);
    }

    [Fact]
    public void The_delayed_fill_window_is_five_minutes_after_the_first_fill_minute_not_after_the_decision()
    {
        // 10:00 to 10:05 hold no trade; 10:06 is the first that does (beyond the five minutes of the base rule), so the base misses and a delay of one cannot reach the second one in time
        var hand = Hand.Buy("late", 100.00, 99.00).Rows(Empty("10:00"), Empty("10:01"), Empty("10:02"), Empty("10:03"), Empty("10:04"), Empty("10:05"), Row("10:06", 100.00, 100.05, 99.95, 100.00), Row("10:07", 100.00, 100.05, 99.95, 100.00));

        Walk(hand, 0).NoTrade.ShouldBe(NoTradeReason.NoFillBar);
        Walk(hand, 1).Trade.ShouldBeNull();
    }
}
