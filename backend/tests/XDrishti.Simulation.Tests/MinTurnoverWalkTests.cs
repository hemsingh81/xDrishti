using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>CF-05, ADR 0230: the filter min_turnover in the walker. At or above the threshold the entry is taken; below it, or with an unknown median, it is blocked and counted, never passed.</summary>
public sealed class MinTurnoverWalkTests
{
    private const decimal Twenty = 20m * LiquidityTiers.Crore;

    private static FillRules Rules(decimal? crore, bool conservative = false, decimal a = 3m, decimal b = 8m) =>
        FillRules.Create(0.1, SlippageConfig.Create(1, 0.10, 2).Value, 0, conservative, a, b, crore).Value;

    private static Hand OneTrade() => Hand.Buy("turnover", 1000.00, 990.00).Rows(Row("10:00", 1000.00, 1004.00, 999.95, 1000.00));

    private static string Row(string time, double open, double high, double low, double close) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{time} {open}/{high}/{low}/{close}");

    [Fact]
    public void The_median_equal_to_the_threshold_passes_and_a_paisa_under_it_blocks()
    {
        OneTrade().Walk(Rules(20m), null, _ => Twenty).Trade.ShouldNotBeNull();

        var blocked = OneTrade().Walk(Rules(20m), null, _ => Twenty - 0.01m);
        blocked.Trade.ShouldBeNull();
        blocked.NoTrade.ShouldBe(NoTradeReason.TurnoverBlocked);
        blocked.ToCounters().TurnoverBlocked.ShouldBe(1);
    }

    [Fact]
    public void An_unknown_median_and_a_missing_lookup_block_and_never_pass()
    {
        foreach (Func<DateOnly, decimal?>? lookup in new Func<DateOnly, decimal?>?[] { _ => null, null })
        {
            var outcome = OneTrade().Walk(Rules(20m), null, lookup);

            outcome.Trade.ShouldBeNull();
            outcome.NoTrade.ShouldBe(NoTradeReason.TurnoverBlocked);
        }
    }

    [Fact]
    public void A_run_without_the_threshold_never_reads_the_lookup_and_its_fills_are_unchanged()
    {
        var read = false;
        var plain = OneTrade().Walk(Rules(null), null, _ => { read = true; return 0m; });

        plain.Trade.ShouldNotBeNull();
        read.ShouldBeFalse();
        plain.Trade.EntryFilledPrice.ShouldBe(OneTrade().Walk(Rules(20m), null, _ => Twenty).Trade!.EntryFilledPrice);
    }

    [Fact]
    public void The_tier_bps_of_the_rules_price_the_entry_and_the_default_equals_the_old_constant()
    {
        var byDefault = OneTrade().Walk(Rules(null, true), _ => LiquidityTier.A).Trade!;
        var wider = OneTrade().Walk(Rules(null, true, a: 5m), _ => LiquidityTier.A).Trade!;

        byDefault.EntryFilledPrice.ShouldBe(1000.75, "3 bps as before");
        wider.EntryFilledPrice.ShouldBeGreaterThan(byDefault.EntryFilledPrice, "5 bps costs more");
        Rules(null, true).BpsOf(LiquidityTier.B).ShouldBe(8m);
    }
}
