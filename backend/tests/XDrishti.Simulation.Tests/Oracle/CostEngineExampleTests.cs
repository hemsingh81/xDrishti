using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Annex C of P3-08, the three-way agreement on the worked examples of P3-03c (W1 to W8, W1b, L1): the printed numbers are the author's own data (<see cref="CostExamples"/>, copied from the spec), the oracle equals them
/// (<c>CostExampleTests</c>) and here the engine, reached through <see cref="CostAdapter"/>, equals the same numbers line by line; and the TEST sheet that the author typed from the spec table equals the engine's sheet.
/// </summary>
public sealed class CostEngineExampleTests
{
    public static TheoryData<string> Ids()
    {
        var d = new TheoryData<string>();
        foreach (var e in CostExamples.All)
        {
            d.Add(e.Id);
        }

        return d;
    }

    private static CostView Engine(CostExample e, decimal maxCostR)
    {
        var trade = CostAdapter.EngineTrade(e.Input);
        return CostView.Of(CostAdapter.Apply(trade, TestCostSheet.Sheet, CostAdapter.Profile(e.Sizing), e.Input.Capital, maxCostR, CostAdapter.Segment(e.Input.Segment)));
    }

    /// <summary>The differences between the engine's costing and the printed numbers of the example. A skipped example prints its legs too, so the engine is asked once with the example's limit (the fate) and once with the engine's widest limit (the lines).</summary>
    private static List<string> Mismatches(CostExample e)
    {
        var d = new List<string>();
        var fate = Engine(e, e.Input.MaxCostR).Fate;
        var wantFate = e.ExpectedSkip switch
        {
            OracleCostSkip.None => CostView.Kept,
            OracleCostSkip.Quantity => CostView.SkipQuantity,
            _ => CostView.SkipCost,
        };
        if (fate != wantFate)
        {
            d.Add($"fate {fate} != {wantFate}");
        }

        var open = Engine(e, CostAdapter.EngineMaxCostR);
        if (e.ExpectedSkip == OracleCostSkip.Quantity)
        {
            if (open.Fate != CostView.SkipQuantity)
            {
                d.Add($"open fate {open.Fate}");
            }

            return d;
        }

        if (open.Fate != CostView.Kept)
        {
            d.Add($"open fate {open.Fate}");
            return d;
        }

        Eq(d, "quantity", open.Quantity, e.ExpectedQuantity);
        Opt(d, "riskTaken", open.RiskTaken, e.RiskTaken);
        Opt(d, "entryTurnover", open.EntryTurnover, e.EntryTurnover);
        Opt(d, "exitTurnover", open.ExitTurnover, e.ExitTurnover);
        if (e.Entry is { } en)
        {
            Eq(d, "entry", [open.EntryBrokerage, open.EntryStt, open.EntryExchange, open.EntrySebi, open.EntryStamp, open.EntryOther, open.EntryDp, open.EntryGst, open.EntryTotal], [en.Brokerage, en.Stt, en.Exchange, en.Sebi, en.Stamp, en.Other, en.Dp, en.Gst, en.Total]);
        }

        if (e.Exit is { } ex)
        {
            Eq(d, "exit", [open.ExitBrokerage, open.ExitStt, open.ExitExchange, open.ExitSebi, open.ExitStamp, open.ExitOther, open.ExitDp, open.ExitGst, open.ExitTotal], [ex.Brokerage, ex.Stt, ex.Exchange, ex.Sebi, ex.Stamp, ex.Other, ex.Dp, ex.Gst, ex.Total]);
        }

        if (e.TotalCost is { } total)
        {
            Eq(d, "total", open.TotalCost, total);
        }

        if (e.CostR is { } costR && Math.Abs((decimal)open.CostR - costR) > e.CostRTolerance)
        {
            d.Add($"costR {open.CostR} != {costR}");
        }

        if (e.RNet is { } net && Math.Abs(open.RNet - net) > e.RNetTolerance)
        {
            d.Add($"rNet {open.RNet} != {net}");
        }

        return d;
    }

    private static void Eq(List<string> d, string name, decimal[] got, decimal[] want)
    {
        if (!got.SequenceEqual(want))
        {
            d.Add($"{name} [{string.Join(", ", got)}] != [{string.Join(", ", want)}]");
        }
    }

    private static void Eq<T>(List<string> d, string name, T got, T want)
    {
        if (!EqualityComparer<T>.Default.Equals(got, want))
        {
            d.Add($"{name} {got} != {want}");
        }
    }

    private static void Opt(List<string> d, string name, decimal got, decimal? want)
    {
        if (want is { } w)
        {
            Eq(d, name, got, w);
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_engine_equals_every_printed_line_of_the_example(string id)
    {
        var e = CostExamples.All.Single(x => x.Id == id);

        Mismatches(e).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_engine_and_the_oracle_agree_on_the_example_in_every_field(string id)
    {
        var e = CostExamples.All.Single(x => x.Id == id);
        var oracle = CostView.Of(ReferenceCosting.Cost(e.Input, TestSheet.Sheet, e.Sizing), e.Input.Segment, TestCostSheet.Sheet.Id);
        var engine = Engine(e, e.Input.MaxCostR);

        CostComparer.Diff(oracle, engine, new RatioStats(), id).ShouldBeEmpty();
    }

    [Fact]
    public void The_TEST_sheet_the_author_typed_from_the_spec_table_is_the_engine_s_sheet()
    {
        var engine = CostAdapter.Sheet(TestCostSheet.Sheet);

        engine.Schedules.Count.ShouldBe(TestSheet.Sheet.Schedules.Count);
        engine.Schedules.ShouldBe(TestSheet.Sheet.Schedules);
        engine.Id.ShouldBe(TestSheet.Sheet.Id);
    }

    [Fact]
    public void The_sheet_adapter_round_trips_the_TEST_sheet_and_the_fixture_sheet()
    {
        foreach (var sheet in new[] { TestCostSheet.Sheet, FixtureSheet.Sheet })
        {
            CostAdapter.EngineSheet(CostAdapter.Sheet(sheet)).Schedules.ShouldBe(sheet.Schedules);
        }
    }

    [Fact]
    public void A_date_before_the_first_schedule_is_the_same_error_on_both_sides_never_a_zero_cost_trade()
    {
        var e = CostExamples.All.Single(x => x.Id == "W1");
        var input = e.Input with { EntryDate = new DateOnly(2025, 6, 2), ExitDate = new DateOnly(2025, 6, 2) };

        var oracle = Should.Throw<InvalidOperationException>(() => ReferenceCosting.Cost(input, TestSheet.Sheet, e.Sizing));
        var engine = CostAdapter.Apply(CostAdapter.EngineTrade(input), TestCostSheet.Sheet, SizingProfile.Standard, input.Capital, input.MaxCostR, CostSegment.IntradayEquity);

        engine.IsFailure.ShouldBeTrue();
        oracle.Message.ShouldStartWith(engine.Error.Code);
        engine.Error.Code.ShouldBe("costs.no_rates_for_date");
    }

    [Theory]
    [InlineData(2026, 3, 2, 2026, 3, 2)]
    [InlineData(2026, 3, 2, 2026, 3, 3)]
    [InlineData(2026, 3, 3, 2026, 3, 3)]
    [InlineData(2026, 3, 3, 2026, 3, 4)]
    [InlineData(2026, 1, 1, 2026, 1, 1)]
    [InlineData(2026, 3, 2, 2026, 3, 9)]
    public void Each_leg_is_charged_on_the_schedule_in_force_on_its_own_date_on_both_sides_including_the_day_a_schedule_starts(int ey, int em, int ed, int xy, int xm, int xd)
    {
        var w1 = CostExamples.All.Single(x => x.Id == "L1");
        var input = w1.Input with { EntryDate = new DateOnly(ey, em, ed), ExitDate = new DateOnly(xy, xm, xd) };

        var oracle = CostView.Of(ReferenceCosting.Cost(input, TestSheet.Sheet, w1.Sizing), input.Segment, TestCostSheet.Sheet.Id);
        var engine = CostView.Of(CostAdapter.Apply(CostAdapter.EngineTrade(input), TestCostSheet.Sheet, SizingProfile.Standard, input.Capital, input.MaxCostR, CostSegment.Delivery));

        CostComparer.Diff(oracle, engine, new RatioStats(), "dates").ShouldBeEmpty();
        oracle.Fate.ShouldBe(CostView.Kept);
    }

    [Fact]
    public void The_depository_charge_is_the_sheet_s_figure_taken_as_printed_not_rounded_again_and_carries_no_GST_on_both_sides()
    {
        // the author's question Q18: a figure of three places is neither rounded nor taxed, by the oracle or by the engine
        var rates = TestCostSheet.Rates(0m, 0m, 0m, 0m, 0m, 0.004m, 50m, 0m, 0m, 13.555m);
        var engineSheet = TestCostSheet.Of("test-dp", TestCostSheet.Schedule(new DateOnly(2026, 1, 1), rates, rates));
        var input = CostExamples.All.Single(x => x.Id == "L1").Input with { MaxCostR = 1m };

        var oracle = CostView.Of(ReferenceCosting.Cost(input, CostAdapter.Sheet(engineSheet), OracleSizing.Standard), input.Segment, engineSheet.Id);
        var engine = CostView.Of(CostAdapter.Apply(CostAdapter.EngineTrade(input), engineSheet, SizingProfile.Standard, input.Capital, input.MaxCostR, CostSegment.Delivery));

        engine.ExitDp.ShouldBe(13.555m);
        engine.EntryDp.ShouldBe(0m);
        CostComparer.Diff(oracle, engine, new RatioStats(), "dp").ShouldBeEmpty();
    }

    [Fact]
    public void The_ten_named_limits_of_the_costing_are_the_same_codes_in_the_same_order_the_author_read_from_the_ADR()
    {
        // the gate statement of D17 (a swing trade that costing skipped still counts as the position) is pinned on the engine by CostedReplayTests; here the list the author wrote from the ADR is set against the engine's
        CostLimits.NotModelled.Select(l => l.Code).ShouldBe(OracleCostLimits.NotModelled.Select(l => l.Code));
        CostLimits.NotModelled[^1].Code.ShouldBe(OracleCostLimits.SwingGateBeforeCosting);
    }

    [Fact]
    public void The_quantity_skip_comes_before_the_cost_skip_on_both_sides()
    {
        // a trade with no share to buy and a limit that no charge could meet: the fate is the quantity skip (a trade with no quantity has no legs to cost)
        var w4 = CostExamples.All.Single(x => x.Id == "W4");
        var input = w4.Input with { MaxCostR = 0.0000001m };

        var oracle = CostView.Of(ReferenceCosting.Cost(input, TestSheet.Sheet, w4.Sizing), input.Segment, TestCostSheet.Sheet.Id);
        var engine = CostView.Of(CostAdapter.Apply(CostAdapter.EngineTrade(input), TestCostSheet.Sheet, SizingProfile.Standard, input.Capital, input.MaxCostR, CostSegment.IntradayEquity));

        oracle.Fate.ShouldBe(CostView.SkipQuantity);
        engine.Fate.ShouldBe(CostView.SkipQuantity);
    }
}
