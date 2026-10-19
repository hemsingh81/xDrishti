namespace XDrishti.Simulation.Tests.Oracle;

public sealed class CostSeriesTests(ITestOutputHelper output)
{
    private const int Seeds = 1000;

    [Fact]
    public void Every_category_is_reached_in_at_least_500_cases_by_the_trace()
    {
        foreach (OracleCostCategory c in Enum.GetValues<OracleCostCategory>())
        {
            string id = OracleCostCategories.TraceId(c);
            int hits = 0;
            for (int seed = 1; seed <= Seeds; seed++)
            {
                if (OracleCostSeries.Of(c, seed).Run().Trace.Contains(id))
                {
                    hits++;
                }
            }

            output.WriteLine($"{c} ({id}): {hits} of {Seeds}");
            hits.ShouldBeGreaterThanOrEqualTo(500, c.ToString());
        }
    }

    [Fact]
    public void The_generator_is_deterministic_and_seeds_differ()
    {
        OracleCostCase a = OracleCostSeries.Of(OracleCostCategory.BuyTrade, 7);
        OracleCostCase b = OracleCostSeries.Of(OracleCostCategory.BuyTrade, 7);
        a.Input.ShouldBe(b.Input);
        a.SyntheticSheet.Schedules.ShouldBe(b.SyntheticSheet.Schedules);
        a.Run().TotalCost.ShouldBe(b.Run().TotalCost);
        OracleCostSeries.Of(OracleCostCategory.BuyTrade, 7).Input.ShouldNotBe(OracleCostSeries.Of(OracleCostCategory.BuyTrade, 8).Input);
    }

    [Fact]
    public void The_synthetic_sheets_obey_the_closed_brokerage_rule()
    {
        foreach (OracleCostCategory c in Enum.GetValues<OracleCostCategory>())
        {
            for (int seed = 1; seed <= 200; seed++)
            {
                OracleCostCase k = OracleCostSeries.Of(c, seed);
                k.SyntheticSheet.Id.ShouldStartWith("synthetic-");
                foreach (OracleSchedule s in k.SyntheticSheet.Schedules)
                {
                    foreach (OracleRates r in new[] { s.Intraday, s.Delivery })
                    {
                        if (r.BrokerageCap == 0m)
                        {
                            r.BrokeragePct.ShouldBe(0m);
                            r.BrokerageMin.ShouldBe(0m);
                        }
                        else
                        {
                            r.BrokerageMin.ShouldBeLessThanOrEqualTo(r.BrokerageCap);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Properties_hold_on_every_generated_case()
    {
        foreach (OracleCostCategory c in Enum.GetValues<OracleCostCategory>())
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                OracleCostCase k = OracleCostSeries.Of(c, seed);
                OracleCostResult r = k.Run();
                string where = $"{c} {seed}";
                long q = ReferenceCosting.Quantity(k.Input.Capital, k.Sizing, k.Input.EntryExact, k.Input.StopPrice);
                r.Quantity.ShouldBe(q, where);
                if (q < 1)
                {
                    r.Skip.ShouldBe(OracleCostSkip.Quantity, where);
                    r.RNet.ShouldBeNull(where);
                    continue;
                }

                // floor: the risk taken never exceeds the budget; the notional bound holds on the exact entry.
                r.RiskTaken.ShouldBeLessThanOrEqualTo(r.RiskAmount, where);
                (q * k.Input.EntryExact).ShouldBeLessThanOrEqualTo(k.Input.Capital * k.Sizing.MaxCapitalPct / 100m, where);
                r.TotalCost.ShouldBe(r.EntryLeg!.Total + r.ExitLeg!.Total, where);
                (r.TotalCost % 0.01m).ShouldBe(0m, where);
                r.CostR.ShouldBe(r.TotalCost / r.RiskTaken, where);
                if (k.Input.Segment == OracleCostSegment.IntradayEquity)
                {
                    r.ExitLeg.Dp.ShouldBe(0m, where);
                }

                if (r.CostR > k.Input.MaxCostR)
                {
                    r.Skip.ShouldBe(OracleCostSkip.Cost, where);
                    r.RNet.ShouldBeNull(where);
                }
                else
                {
                    r.Skip.ShouldBe(OracleCostSkip.None, where);
                    r.RNet.ShouldBe(k.Input.RFilled - r.CostRDouble, where);
                    r.RNet!.Value.ShouldBeLessThanOrEqualTo(k.Input.RFilled, where);
                }
            }
        }
    }

    // The test that switches the faults on over generated cases is in CostFaultTests: only classes with that suffix pass faults (the architecture test of ADR 0047).
}
