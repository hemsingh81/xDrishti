using System.Diagnostics;
using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 12, 14 and 15 of P3-03c over the 500 seeded days of P3-03b (both sides, ticks 0.01, 0.05 and 0.10, no slippage and the doc 16 slippage, capitals from 10,000 to 1,00,00,000, both schedules): the properties of net R, the
/// skips and their order, and the unit of R that the walker and the costing share. The walker's trades are costed directly by <see cref="TradeCostApplier"/>; the replay's use of it is in <c>CostedReplayTests</c>.
/// </summary>
public sealed class CostedPropertyTests
{
    private static readonly decimal[] Capitals = [10_000m, 100_000m, 1_000_000m, 10_000_000m];

    private static readonly decimal[] Limits = [0.01m, 0.05m, 0.10m, 0.25m, 100m];

    private static readonly FillRules NoSlippage = FillRules.Create(0.1, SlippageConfig.None).Value;

    /// <summary>The seeded days walked with no slippage and with the defaults; the trades only.</summary>
    private static readonly List<SimTrade> Trades = [.. WalkProperties.WalkAll(SeededDays.All, NoSlippage).Concat(WalkProperties.WalkAll(SeededDays.All, FillRules.Default)).Select(w => w.Trade).OfType<SimTrade>()];

    /// <summary>Schedule A on every trade of the seeded days (dated 2026-03-02).</summary>
    private static readonly CostSheet SheetA = TestCostSheet.Sheet;

    /// <summary>The same sheet with schedule B in force from 2026-03-02, so that the trades are charged on the second schedule too.</summary>
    private static readonly CostSheet SheetB = TestCostSheet.Of("test-synthetic-b", TestCostSheet.Schedule(new DateOnly(2026, 3, 2), TestCostSheet.IntradayB, TestCostSheet.DeliveryB));

    private static CostApplication Apply(SimTrade trade, CostSheet sheet, decimal capital, decimal maxCostR)
    {
        var applied = TradeCostApplier.Apply(trade, TestCostSheet.Context(capital, maxCostR, sheet: sheet), CostSegment.IntradayEquity);
        applied.IsSuccess.ShouldBeTrue(applied.IsFailure ? applied.Error.Code : string.Empty);
        return applied.Value;
    }

    [Fact]
    public void The_seeded_days_give_enough_trades_on_both_sides_to_mean_something()
    {
        Trades.Count.ShouldBeGreaterThan(1_500);
        Trades.Select(t => t.Side).Distinct().Count().ShouldBe(2);
        Trades.Select(t => t.Fills).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void Net_R_is_not_above_filled_R_which_is_not_above_exact_R_and_is_below_it_whenever_something_was_charged()
    {
        long kept = 0;
        foreach (var trade in Trades)
        {
            foreach (var sheet in new[] { SheetA, SheetB })
            {
                foreach (var capital in Capitals)
                {
                    if (Apply(trade, sheet, capital, 100m).Trade is not { } costed)
                    {
                        continue;
                    }

                    kept++;
                    var costing = costed.Costing!;
                    costing.RNet.ShouldBeLessThanOrEqualTo(costed.RFilled);
                    costed.RFilled.ShouldBeLessThanOrEqualTo(costed.RExact + 1e-12);
                    if (costing.TotalCost > 0)
                    {
                        costing.RNet.ShouldBeLessThan(costed.RFilled);
                    }
                    else
                    {
                        costing.RNet.ShouldBe(costed.RFilled);
                    }

                    costing.RNet.ShouldBe(costed.RFilled - costing.CostR, "net R is exactly the filled R less the charge in R");
                }
            }
        }

        kept.ShouldBeGreaterThan(10_000);
    }

    [Fact]
    public void The_charge_in_R_is_the_total_over_the_risk_taken_never_above_the_limit_and_the_total_is_the_sum_of_the_sixteen_lines()
    {
        foreach (var trade in Trades.Where((_, i) => i % 3 == 0))
        {
            foreach (var limit in Limits)
            {
                if (Apply(trade, SheetA, 1_000_000m, limit).Trade is not { } costed)
                {
                    continue;
                }

                var c = costed.Costing!;
                var lines = new[] { c.EntryLeg, c.ExitLeg }.SelectMany(l => new[] { l.Brokerage, l.Stt, l.ExchangeTxn, l.SebiFee, l.StampDuty, l.OtherCharges, l.DpCharge, l.Gst }).ToList();
                lines.Count.ShouldBe(16);
                c.TotalCost.ShouldBe(lines.Sum());
                (c.TotalCost % 0.01m).ShouldBe(0m, "a multiple of the paisa");
                c.CostR.ShouldBe((double)(c.TotalCost / c.RiskTaken), 1e-12);
                c.CostR.ShouldBeGreaterThanOrEqualTo(0);
                (c.TotalCost / c.RiskTaken).ShouldBeLessThanOrEqualTo(limit);
                c.Quantity.ShouldBeGreaterThanOrEqualTo(1);
                (c.RiskTaken / c.Quantity).ShouldBe(Math.Abs(Price(costed.EntryPrice) - Price(costed.StopPrice)), "the risk per share is the walker's unit of R");
                c.EntryTurnover.ShouldBe(c.Quantity * Price(costed.EntryFilledPrice));
                c.ExitTurnover.ShouldBe(c.Quantity * Price(costed.ExitFilledPrice));
                (costed.Costs == CostBasis.Charged).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void Kept_plus_the_two_skips_is_exactly_the_walkers_trades_and_the_walkers_fields_are_untouched()
    {
        foreach (var capital in Capitals)
        {
            foreach (var limit in Limits)
            {
                long kept = 0, quantity = 0, cost = 0;
                foreach (var trade in Trades)
                {
                    var applied = Apply(trade, SheetA, capital, limit);
                    if (applied.Trade is { } costed)
                    {
                        kept++;
                        (costed with { Costing = null, Costs = CostBasis.None }).ShouldBe(trade);
                    }
                    else if (applied.Skip == TradeSkip.QuantityBelowOne)
                    {
                        quantity++;
                    }
                    else
                    {
                        applied.Skip.ShouldBe(TradeSkip.CostAboveMax);
                        cost++;
                    }
                }

                (kept + quantity + cost).ShouldBe(Trades.Count, $"capital {capital}, limit {limit}: none lost or invented");
            }
        }
    }

    [Fact]
    public void A_trade_that_fails_both_checks_is_a_quantity_skip_and_raising_the_limit_never_raises_the_cost_skips_nor_the_capital_the_quantity_skips()
    {
        var byLimit = Limits.Select(limit => Trades.Count(t => Apply(t, SheetA, 100_000m, limit).Skip == TradeSkip.CostAboveMax)).ToArray();
        var byCapital = Capitals.Select(capital => Trades.Count(t => Apply(t, SheetA, capital, 100m).Skip == TradeSkip.QuantityBelowOne)).ToArray();

        byLimit.Zip(byLimit.Skip(1), (a, b) => a >= b).ShouldAllBe(x => x, "cost skips by limit: " + string.Join(",", byLimit));
        byCapital.Zip(byCapital.Skip(1), (a, b) => a >= b).ShouldAllBe(x => x, "quantity skips by capital: " + string.Join(",", byCapital));
        byLimit[0].ShouldBeGreaterThan(0);
        byLimit[^1].ShouldBe(0);
        byCapital[0].ShouldBeGreaterThan(0, "at 10,000 some seeded prices cost more than the cap of a share");
        foreach (var trade in Trades.Where((_, i) => i % 7 == 0))
        {
            var failsBoth = Apply(trade, SheetA, 10_000m, 0.0001m);
            if (failsBoth.Skip == TradeSkip.CostAboveMax)
            {
                Apply(trade, SheetA, 10_000m, 100m).Trade.ShouldNotBeNull("a trade skipped by cost has a quantity of at least 1");
            }
        }
    }

    [Fact]
    public void The_risk_of_the_walker_and_of_the_costing_is_the_same_so_exact_R_is_recomputed_from_the_double_fields_to_one_in_a_billion()
    {
        foreach (var trade in Trades)
        {
            var entry = Price(trade.EntryPrice);
            var risk = Math.Abs(entry - Price(trade.StopPrice));
            var direction = trade.Side == Side.Buy ? 1m : -1m;

            // the exit of a target is entry + reward-to-risk x risk and can have a ninth decimal, so it is read as the double holds it (15 significant digits) and not rounded to 8 places; the entry and the stop are 8-place values in the walker
            var recomputed = direction * ((decimal)trade.ExitPrice - entry) / risk;

            ((double)recomputed).ShouldBe(trade.RExact, 1e-9, trade.Key);
            risk.ShouldBeGreaterThan(0m);
        }
    }

    [Fact]
    public void Costing_is_deterministic_and_the_text_of_two_costings_is_identical()
    {
        var first = Trades.Take(300).Select(t => Apply(t, SheetA, 1_000_000m, 100m).Trade).OfType<SimTrade>().Select(t => RunText.Trade(t)).ToList();
        var second = Trades.Take(300).Select(t => Apply(t with { }, SheetA, 1_000_000m, 100m).Trade).OfType<SimTrade>().Select(t => RunText.Trade(t)).ToList();

        second.ShouldBe(first);
        first.ShouldNotBeEmpty();
        first.ShouldAllBe(text => !RunText.HasNegativeZero(text));
    }

    /// <summary>
    /// An explicit benchmark, not part of <c>scripts/check.sh</c>: reports trades costed per second, asserts nothing. Run in Release:
    /// <c>dotnet test --project tests/XDrishti.Simulation.Tests -c Release -- --explicit only --filter-method "*Costing_throughput*"</c>.
    /// </summary>
    [Fact(Explicit = true)]
    public void Costing_throughput_is_reported()
    {
        var context = TestCostSheet.Context(1_000_000m, 100m);
        long costed = 0;
        var watch = Stopwatch.StartNew();
        for (var round = 0; round < 50; round++)
        {
            foreach (var trade in Trades)
            {
                if (TradeCostApplier.Apply(trade, context, CostSegment.IntradayEquity).Value.Trade is not null)
                {
                    costed++;
                }
            }
        }

        watch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:N0} trades costed in {1:N0} ms: {2:N0} trades per second", costed, watch.ElapsedMilliseconds, costed / watch.Elapsed.TotalSeconds));
    }

    private static decimal Price(double price) => Math.Round((decimal)price, 8);
}
