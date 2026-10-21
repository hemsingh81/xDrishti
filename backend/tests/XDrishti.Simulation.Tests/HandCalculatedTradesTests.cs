using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 5 to 10, 14 and 15: the 25 trades H1 to H25 of P3-03b, every filled price, R, MAE, MFE and counter equal to the table, in the modelled run with the doc 16 defaults and again with no slippage (where the filled layer is
/// the exact one and everything else is the table's). Prices are compared as decimals rounded to 4 places; R, MAE and MFE are recomputed from the prices as decimal quotients.
/// </summary>
public sealed class HandCalculatedTradesTests
{
    public static TheoryData<string> Names => [.. HandCases.All.Select(c => c.Hand.Name)];

    private static decimal Q(double value) => Math.Round((decimal)value, 4);

    [Theory]
    [MemberData(nameof(Names))]
    public void In_the_modelled_run_every_price_ratio_and_counter_equals_the_table(string name)
    {
        var (hand, want) = HandCases.Get(name);

        var outcome = hand.Walk(FillRules.Default);

        var trade = outcome.Trade.ShouldNotBeNull(name);
        Q(trade.EntryPrice).ShouldBe(want.Entry, name + " entry");
        Q(trade.EntryFilledPrice).ShouldBe(want.EntryFilled, name + " entry filled");
        Q(trade.ExitPrice).ShouldBe(want.Exit, name + " exit");
        Q(trade.ExitFilledPrice).ShouldBe(want.ExitFilled, name + " exit filled");
        trade.ExitReason.ShouldBe(want.Reason, name);
        trade.EntryFill.ShouldBe(want.Kind, name);
        Q(trade.RExact).ShouldBe(want.RExact, name + " RExact");
        Q(trade.RFilled).ShouldBe(want.RFilled, name + " RFilled");
        Q(trade.SlippageR).ShouldBe(want.SlippageR, name + " SlippageR");
        Q(trade.MaeR).ShouldBe(want.Mae, name + " MAE");
        Q(trade.MfeR).ShouldBe(want.Mfe, name + " MFE");
        trade.Fills.ShouldBe(FillBasis.Modelled, name);
        trade.Costs.ShouldBe(CostBasis.None, name);
        trade.TickFallback.ShouldBeFalse(name);
        trade.SchemaVersion.ShouldBe(5);
        AssertCounters(name, outcome, want);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_ratios_are_the_quotients_of_the_prices_in_decimal_and_the_unit_is_the_exact_ticket(string name)
    {
        var (hand, _) = HandCases.Get(name);

        var trade = hand.Walk(FillRules.Default).Trade.ShouldNotBeNull(name);

        var sign = trade.Side == Side.Buy ? 1m : -1m;
        var risk = Math.Abs((decimal)trade.EntryPrice - (decimal)trade.StopPrice);
        risk.ShouldBeGreaterThan(0m, name);
        ((double)(sign * ((decimal)trade.ExitPrice - (decimal)trade.EntryPrice) / risk)).ShouldBe(trade.RExact, 1e-9, name);
        ((double)(sign * ((decimal)trade.ExitFilledPrice - (decimal)trade.EntryFilledPrice) / risk)).ShouldBe(trade.RFilled, 1e-9, name);
        (trade.RFilled - trade.RExact).ShouldBe(trade.SlippageR, 1e-9, name);
        trade.SlippageR.ShouldBeLessThanOrEqualTo(0, name);
        (-trade.MaeR).ShouldBeLessThanOrEqualTo(trade.RExact + 1e-12, name);
        trade.RExact.ShouldBeLessThanOrEqualTo(trade.MfeR + 1e-12, name);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void With_no_slippage_the_filled_layer_is_the_exact_one_and_the_rest_is_the_tables(string name)
    {
        var (hand, want) = HandCases.Get(name);

        var outcome = hand.Walk(FillRules.Create(0.1, SlippageConfig.None).Value);

        var trade = outcome.Trade.ShouldNotBeNull(name);
        trade.EntryFilledPrice.ShouldBe(trade.EntryPrice, name);
        trade.ExitFilledPrice.ShouldBe(trade.ExitPrice, name);
        trade.RFilled.ShouldBe(trade.RExact, name);
        trade.SlippageR.ShouldBe(0, name);
        trade.Fills.ShouldBe(FillBasis.Exact, name);
        Q(trade.EntryPrice).ShouldBe(want.Entry, name);
        Q(trade.ExitPrice).ShouldBe(want.Exit, name);
        trade.ExitReason.ShouldBe(want.Reason, name);
        trade.EntryFill.ShouldBe(want.Kind, name);
        Q(trade.RExact).ShouldBe(want.RExact, name);
        Q(trade.MaeR).ShouldBe(want.Mae, name);
        Q(trade.MfeR).ShouldBe(want.Mfe, name);
        AssertCounters(name, outcome, want);
    }

    [Fact]
    public void Risk_under_four_ticks_is_counted_for_H25_and_for_no_other_trade()
    {
        HandCases.All.Where(c => c.Hand.Walk(FillRules.Default).RiskUnderFourTicks).Select(c => c.Hand.Name).ShouldBe(["H25"]);
    }

    [Fact]
    public void The_stop_of_a_trade_is_the_candidates_level_unrounded_and_every_target_is_on_the_exact_decimal()
    {
        // H21: entry 100.00, stop 98.85, k 2: the target is 102.30, not the double 102.30000000000001; H22 (sell): 197.75, not 197.74999999999997.
        HandCases.Get("H21").Hand.Walk(FillRules.Default).Trade!.TargetPrice.ShouldBe(102.30);
        HandCases.Get("H22").Hand.Walk(FillRules.Default).Trade!.TargetPrice.ShouldBe(197.75);
        HandCases.Get("H23").Hand.Walk(FillRules.Default).Trade!.StopPrice.ShouldBe(99.05);
    }

    [Fact]
    public void Resting_limits_do_not_slip_an_entry_at_the_limit_and_a_target_exit_only_go_on_the_grid()
    {
        foreach (var name in new[] { "H2", "H13" })
        {
            var trade = HandCases.Get(name).Hand.Walk(FillRules.Default).Trade!;
            trade.EntryFill.ShouldBe(EntryFill.AtLimit, name);
            trade.EntryFilledPrice.ShouldBe(trade.EntryPrice, name);
        }

        foreach (var name in new[] { "H1", "H15", "H21", "H22" })
        {
            var trade = HandCases.Get(name).Hand.Walk(FillRules.Default).Trade!;
            trade.ExitReason.ShouldBe(ExitReason.Target, name);
            trade.ExitFilledPrice.ShouldBe(trade.ExitPrice, name);
        }
    }

    [Fact]
    public void An_entry_at_the_open_is_capped_at_the_limit()
    {
        var trade = HandCases.Get("H15").Hand.Walk(FillRules.Default).Trade!;

        Q(trade.EntryFilledPrice).ShouldBe(100.10m);
        trade.EntryPrice.ShouldBe(100.05);
    }

    [Fact]
    public void A_square_off_minute_that_opens_beyond_the_target_exits_at_the_target()
    {
        // Setup A, then 15:15 102.40/102.50/102.30/102.40: the target 102.00 fills (criterion 10), TargetGapExits 1.
        var outcome = Hand.Buy("X1-target").Rows("10:00 100.00/100.30/99.80/100.20", "15:15 102.40/102.50/102.30/102.40").Walk(FillRules.Default);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.ExitPrice.ShouldBe(102.00);
        outcome.TargetGapExit.ShouldBeTrue();
        trade.RExact.ShouldBe(2.0);
    }

    [Fact]
    public void An_entry_at_the_limit_does_not_exit_at_the_target_in_the_fill_minute_and_its_high_is_left_out_of_mfe()
    {
        // Criterion 9: AtLimit fill minute 10:00 103.00/103.20/100.00/100.40 (entry 100.10, risk 1.10, target 102.30) and flat 100.20 minutes after it.
        var hand = Hand.Buy("X5").Rows("10:00 103.00/103.20/100.00/100.40", "10:01 100.20/100.20/100.20/100.20");

        var outcome = hand.Walk(FillRules.Default);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.EntryFill.ShouldBe(EntryFill.AtLimit);
        trade.EntryPrice.ShouldBe(100.10);
        trade.TargetPrice.ShouldBe(102.30);
        trade.ExitReason.ShouldBe(ExitReason.SquareOff);
        trade.ExitEpochSeconds.ShouldBe(Day.Epoch(Day.Monday, Day.Minute("15:15")));
        trade.ExitPrice.ShouldBe(100.20);
        Q(trade.RExact).ShouldBe(0.0909m);
        Q(trade.MfeR).ShouldBe(0.0909m);
        Q(trade.MaeR).ShouldBe(0.0909m);
    }

    [Fact]
    public void The_sell_mirror_of_an_entry_at_the_limit_leaves_the_low_of_the_fill_minute_out_of_mfe()
    {
        // Mirror of the case above for a sell: limit tickUp(200 x 0.999) = 199.80; fill minute 197.00/200.00/196.80/199.60 opens below the limit and the high touches it; the low can only have come after the fill but
        // is a favourable price that may have come before it, so MFE counts only what happens later.
        var hand = Hand.Sell("X5-sell", 200.00, 202.00).Rows("10:00 197.00/199.90/196.80/199.60", "10:01 199.50/199.50/199.50/199.50");

        var trade = hand.Walk(FillRules.Default).Trade.ShouldNotBeNull();

        trade.EntryFill.ShouldBe(EntryFill.AtLimit);
        trade.EntryPrice.ShouldBe(199.80);
        trade.ExitReason.ShouldBe(ExitReason.SquareOff);
        Q(trade.MfeR).ShouldBe(Math.Round((199.80m - 199.50m) / 2.20m, 4));
        Q(trade.MaeR).ShouldBe(Math.Round((199.90m - 199.80m) / 2.20m, 4));
    }

    [Fact]
    public void Without_a_target_the_policy_has_no_target_exit_and_no_ambiguous_minute()
    {
        var outcome = Hand.Buy("no-target").WithRewardRisk(null).Rows("10:00 100.00/100.30/99.80/100.20", "10:15 100.50/105.00/100.40/100.00", "11:00 99.50/99.60/98.90/99.00").Walk(FillRules.Default);

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.TargetPrice.ShouldBeNull();
        trade.ExitReason.ShouldBe(ExitReason.Stop);
        outcome.AmbiguousMinute.ShouldBeFalse();
    }

    [Fact]
    public void A_flat_minute_slips_by_the_ticks_only()
    {
        // A flat 10:00 minute (high = low): the entry slips by one tick, no range part: 100.00 -> 100.05 (below the limit 100.10).
        var trade = Hand.Buy("flat").Rows("10:00 100.00/100.00/100.00/100.00").Walk(FillRules.Default).Trade.ShouldNotBeNull();

        Q(trade.EntryFilledPrice).ShouldBe(100.05m);
    }

    private static void AssertCounters(string name, WalkOutcome outcome, Want want)
    {
        var counters = outcome.ToCounters();
        counters.EntriesAtOpen.ShouldBe(want.Kind == EntryFill.AtOpen ? 1 : 0, name);
        counters.EntriesAtLimit.ShouldBe(want.Kind == EntryFill.AtLimit ? 1 : 0, name);
        counters.StopGapExits.ShouldBe(want.Fx.StopGap ? 1 : 0, name);
        counters.TargetGapExits.ShouldBe(want.Fx.TargetGap ? 1 : 0, name);
        counters.AmbiguousMinutes.ShouldBe(want.Fx.Ambiguous ? 1 : 0, name);
        counters.LateSquareOffs.ShouldBe(want.Fx.Late ? 1 : 0, name);
        counters.RiskUnderFourTicks.ShouldBe(want.Fx.Under4 ? 1 : 0, name);
        counters.EntriesAfterMissingMinutes.ShouldBe(want.Fx.Missing ? 1 : 0, name);
        counters.ZeroVolumeMinutes.ShouldBe(want.Fx.Zero, name);
        counters.ExitsEndOfData.ShouldBe(want.Reason == ExitReason.EndOfData ? 1 : 0, name);
        counters.LimitNotReached.ShouldBe(0, name);
    }
}
