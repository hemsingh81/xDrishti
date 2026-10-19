using System.Reflection;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Annex C of P3-08: the adapter of the costing is held to account (it is where a misreading of the engine could hollow the agreement), the dry read of the cost record is repeated by reflection (every property of <c>TradeCosting</c> and
/// <c>LegCharges</c> is compared or echoed and has a row in the table of the dry read), and the comparison is shown to be reproducible.
/// </summary>
public sealed class CostAdapterTests
{
    private static OracleCostInput Sample { get; } = new(
        OracleSide.Sell, 199.80m, 199.65m, 202.00m, 195.40m, 2.0, 1.95, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4), OracleCostSegment.Delivery, 1_000_000m, 0.10m);

    [Fact]
    public void The_engine_s_trade_carries_every_field_of_the_oracle_s_input_and_reads_back_the_same_decimals()
    {
        var trade = CostAdapter.EngineTrade(Sample);

        trade.Side.ShouldBe(Side.Sell);
        Math.Round((decimal)trade.EntryPrice, 8).ShouldBe(Sample.EntryExact);
        Math.Round((decimal)trade.EntryFilledPrice, 8).ShouldBe(Sample.EntryFilled);
        Math.Round((decimal)trade.StopPrice, 8).ShouldBe(Sample.StopPrice);
        Math.Round((decimal)trade.ExitFilledPrice, 8).ShouldBe(Sample.ExitFilled);
        trade.TradeDate.ShouldBe(Sample.EntryDate);
        SessionClock.DateOf(trade.ExitEpochSeconds).ShouldBe(Sample.ExitDate);
        trade.RExact.ShouldBe(Sample.RExact);
        trade.RFilled.ShouldBe(Sample.RFilled);
        trade.Costing.ShouldBeNull();
        trade.Costs.ShouldBe(CostBasis.None);
    }

    [Fact]
    public void The_oracle_s_input_from_a_walked_trade_is_the_conversion_of_ADR_0047_R1_for_each_stored_price()
    {
        // 12345678.123456785 keeps 15 significant digits and then rounds to 8 places, to even; 100.000000005 is a decimal midpoint to even
        var walked = new OracleTrade(OracleSide.Buy, new DateOnly(2026, 3, 2), 0, 0, 12345678.123456785, 100.000000005, null, 0, 0, OracleExitReason.Target, 2.0, 0, 0, OracleFillBasis.Modelled, 1.5, 0, 0.123456785, 0.123456775, OracleEntryFill.AtOpen, 1, 0);

        var input = CostAdapter.Input(walked, OracleCostSegment.IntradayEquity, 100_000m, 0.10m);

        input.EntryExact.ShouldBe(12345678.1234568m);
        input.StopPrice.ShouldBe(100.00000000m);
        input.EntryFilled.ShouldBe(0.12345678m);
        input.ExitFilled.ShouldBe(0.12345678m);
        input.ExitDate.ShouldBe(new DateOnly(1970, 1, 1));
    }

    [Fact]
    public void Every_field_of_the_input_changes_what_the_engine_sees_a_field_the_adapter_dropped_would_not()
    {
        var baseline = CostAdapter.EngineTrade(Sample);
        var changed = new Dictionary<string, SimTrade>
        {
            [nameof(OracleCostInput.Side)] = CostAdapter.EngineTrade(Sample with { Side = OracleSide.Buy }),
            [nameof(OracleCostInput.EntryExact)] = CostAdapter.EngineTrade(Sample with { EntryExact = 199.81m }),
            [nameof(OracleCostInput.EntryFilled)] = CostAdapter.EngineTrade(Sample with { EntryFilled = 199.60m }),
            [nameof(OracleCostInput.StopPrice)] = CostAdapter.EngineTrade(Sample with { StopPrice = 202.05m }),
            [nameof(OracleCostInput.ExitFilled)] = CostAdapter.EngineTrade(Sample with { ExitFilled = 195.35m }),
            [nameof(OracleCostInput.RExact)] = CostAdapter.EngineTrade(Sample with { RExact = 2.1 }),
            [nameof(OracleCostInput.RFilled)] = CostAdapter.EngineTrade(Sample with { RFilled = 1.9 }),
            [nameof(OracleCostInput.EntryDate)] = CostAdapter.EngineTrade(Sample with { EntryDate = new DateOnly(2026, 3, 3) }),
            [nameof(OracleCostInput.ExitDate)] = CostAdapter.EngineTrade(Sample with { ExitDate = new DateOnly(2026, 3, 5) }),
        };

        foreach (var (field, trade) in changed)
        {
            trade.ShouldNotBe(baseline, $"the adapter ignores {field}");
        }

        // the three that the engine reads from elsewhere than the trade (segment, capital, limit) are arguments of Apply; the sentinel below shows the comparison notices them
        changed.Keys.Order().ShouldBe(typeof(OracleCostInput).GetProperties().Select(p => p.Name).Where(n => n is not (nameof(OracleCostInput.Segment) or nameof(OracleCostInput.Capital) or nameof(OracleCostInput.MaxCostR) or "EqualityContract")).Order());
    }

    [Fact]
    public void The_sentinel_a_trade_whose_exit_date_is_dropped_is_reported_by_the_comparison()
    {
        // a swing trade over the day a schedule starts: the exit leg is on schedule B. An adapter that lost the exit date would charge both legs on A.
        var input = Sample with { Segment = OracleCostSegment.Delivery, Side = OracleSide.Buy, EntryDate = new DateOnly(2026, 3, 2), ExitDate = new DateOnly(2026, 3, 4), MaxCostR = 1m };
        var dropped = CostAdapter.EngineTrade(input with { ExitDate = input.EntryDate });
        var oracle = CostView.Of(ReferenceCosting.Cost(input, TestSheet.Sheet, OracleSizing.Standard), input.Segment, TestCostSheet.Sheet.Id);
        var honest = CostView.Of(CostAdapter.Apply(CostAdapter.EngineTrade(input), TestCostSheet.Sheet, SizingProfile.Standard, input.Capital, input.MaxCostR, CostSegment.Delivery));
        var lost = CostView.Of(CostAdapter.Apply(dropped, TestCostSheet.Sheet, SizingProfile.Standard, input.Capital, input.MaxCostR, CostSegment.Delivery));

        CostComparer.Diff(oracle, honest, new RatioStats(), "honest").ShouldBeEmpty();
        CostComparer.Diff(oracle, lost, new RatioStats(), "dropped").Select(m => m.Field).ShouldContain(nameof(CostView.ExitStt));
    }

    [Fact]
    public void The_sentinel_a_doctored_view_is_reported_field_by_field()
    {
        var view = CostView.Of(ReferenceCosting.Cost(CostExamples.All.Single(e => e.Id == "W1").Input, TestSheet.Sheet, OracleSizing.Standard), OracleCostSegment.IntradayEquity, TestCostSheet.Sheet.Id);
        var stats = new RatioStats();

        CostComparer.Diff(view, view, stats, "same").ShouldBeEmpty();
        foreach (var property in typeof(CostView).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract"))
        {
            var doctored = Doctor(view, property);
            CostComparer.Diff(view, doctored, stats, property.Name).Select(m => m.Field).ShouldContain(property.Name, $"a changed {property.Name} was not noticed");
        }
    }

    private static CostView Doctor(CostView view, PropertyInfo property)
    {
        var value = property.GetValue(view);
        object changed = value switch
        {
            decimal d => d + 0.01m,
            double d => d + 1e-6,
            long l => l + 1,
            string s => s + "x",
            _ => throw new InvalidOperationException(property.Name),
        };
        var copy = view with { };
        property.SetValue(copy, changed);
        return copy;
    }

    [Fact]
    public void A_negative_zero_against_a_positive_zero_in_the_net_R_is_a_mismatch_whatever_the_tolerance()
    {
        var view = CostView.Of(ReferenceCosting.Cost(CostExamples.All.Single(e => e.Id == "W1").Input, TestSheet.Sheet, OracleSizing.Standard), OracleCostSegment.IntradayEquity, TestCostSheet.Sheet.Id);

        CostComparer.Diff(view with { RNet = 0.0 }, view with { RNet = -0.0 }, new RatioStats(), "zero").Select(m => m.Field).ShouldContain(nameof(CostView.RNet));
    }

    // ---- the dry read of the cost record, by reflection

    /// <summary>The names the comparison reads for a positional property of <c>TradeCosting</c> or <c>LegCharges</c> (the leg properties are read as eight lines for each leg, with the oracle's shorter names).</summary>
    private static IEnumerable<string> ViewNames(string recordProperty) => recordProperty switch
    {
        nameof(TradeCosting.EntryLeg) => LegNames("Entry"),
        nameof(TradeCosting.ExitLeg) => LegNames("Exit"),
        nameof(LegCharges.ExchangeTxn) => ["EntryExchange", "ExitExchange"],
        nameof(LegCharges.SebiFee) => ["EntrySebi", "ExitSebi"],
        nameof(LegCharges.StampDuty) => ["EntryStamp", "ExitStamp"],
        nameof(LegCharges.OtherCharges) => ["EntryOther", "ExitOther"],
        nameof(LegCharges.DpCharge) => ["EntryDp", "ExitDp"],
        nameof(LegCharges.Brokerage) or nameof(LegCharges.Stt) or nameof(LegCharges.Gst) => [$"Entry{recordProperty}", $"Exit{recordProperty}"],
        _ => [recordProperty],
    };

    private static readonly string[] LegLines = ["Brokerage", "Stt", "Exchange", "Sebi", "Stamp", "Other", "Dp", "Gst", "Total"];

    private static IEnumerable<string> LegNames(string leg) => LegLines.Select(n => leg + n);

    [Fact]
    public void Every_property_of_TradeCosting_and_LegCharges_is_compared_or_echoed()
    {
        var properties = FieldCoverageTests.Positional(typeof(TradeCosting)).Concat(FieldCoverageTests.Positional(typeof(LegCharges))).ToList();

        properties.Count.ShouldBe(11 + 8);
        foreach (var property in properties)
        {
            ViewNames(property).ShouldNotBeEmpty();
            FieldCoverageTests.Unread(ViewNames(property), CostComparer.Fields).ShouldBeEmpty($"{property} is not read by the cost comparison");
        }
    }

    [Fact]
    public void Every_property_of_the_cost_record_has_a_row_in_the_dry_read_table()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Oracle", "Docs", "p3-08-field-definitions.md"));

        foreach (var property in FieldCoverageTests.Positional(typeof(TradeCosting)).Concat(FieldCoverageTests.Positional(typeof(LegCharges))))
        {
            (text.Contains("`" + property + "`", StringComparison.Ordinal) || text.Contains("`LegCharges." + property + "`", StringComparison.Ordinal)).ShouldBeTrue($"{property} has no row in section C of the dry-read table");
        }
    }

    [Fact]
    public void The_sentinel_a_property_added_to_the_cost_record_without_a_view_is_reported()
    {
        var withNewField = FieldCoverageTests.Positional(typeof(TradeCosting)).Append("FutureCharge");

        FieldCoverageTests.Unread(withNewField.SelectMany(ViewNames), CostComparer.Fields).ShouldBe(["FutureCharge"]);
    }

    [Fact]
    public void The_two_runs_give_byte_identical_summaries_on_a_second_run_of_the_same_seeds()
    {
        string Series() => CostCorpus.RunSeries(_ => 40).Summary();
        string Chain() => CostCorpus.RunChain(ComparisonCases.Intraday(exact: false).Take(60).Concat(ComparisonCases.Swing(exact: false).Take(60))).Summary();

        Series().ShouldBe(Series());
        Chain().ShouldBe(Chain());
    }
}
