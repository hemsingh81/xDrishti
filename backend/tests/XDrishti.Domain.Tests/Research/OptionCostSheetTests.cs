using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// P14-07 criteria 1, 2 and 9 (domain part): the optional option and futures segments of the charge sheet. The rates below are invented TEST numbers (a <c>test-</c> sheet); no figure of a broker or of the exchange is in <c>src/</c>. A sheet without the
/// segment charges nothing and says so: <c>costs_unavailable</c>, the trade gross, never a zero line.
/// </summary>
public sealed class OptionCostSheetTests
{
    private static readonly DateOnly OptionsFromA = new(2025, 4, 1);
    private static readonly DateOnly OptionsFromB = new(2026, 4, 1);

    // flat 20 per order, STT only on the sell, exchange share, SEBI per crore, stamp on the buy; schedule B raises the sell STT and the exchange share
    private static readonly ChargeRates OptionsA = TestCostSheet.Rates(0m, 20m, 20m, 0m, 0.10m, 0.03m, 10m, 0.003m, 0m, 0m);
    private static readonly ChargeRates OptionsB = TestCostSheet.Rates(0m, 20m, 20m, 0m, 0.15m, 0.035m, 10m, 0.003m, 0m, 0m);

    private static CostSheet OptionSheet() =>
        TestCostSheet.Of(
            "test-options-1",
            ChargeSchedule.Create(OptionsFromA, 18m, ChargeRounding.HalfAwayFromZero, TestCostSheet.IntradayA, TestCostSheet.DeliveryA, OptionsA).Value,
            ChargeSchedule.Create(OptionsFromB, 18m, ChargeRounding.HalfAwayFromZero, TestCostSheet.IntradayB, TestCostSheet.DeliveryB, OptionsB).Value);

    // buy 120.50 x 65 units = 7,832.50; sell 130.25 x 65 units = 8,466.25
    private const decimal BuyTurnover = 7832.50m;
    private const decimal SellTurnover = 8466.25m;

    [Fact]
    public void The_same_buy_sell_pair_pays_the_rates_of_each_date_paise_exact()
    {
        var sheet = OptionSheet();
        var before = new DateOnly(2026, 3, 31);
        var after = new DateOnly(2026, 4, 1);

        var buyA = CostModel.Leg(sheet, CostSegment.Options, Side.Buy, BuyTurnover, before).Value;
        var sellA = CostModel.Leg(sheet, CostSegment.Options, Side.Sell, SellTurnover, before).Value;
        var buyB = CostModel.Leg(sheet, CostSegment.Options, Side.Buy, BuyTurnover, after).Value;
        var sellB = CostModel.Leg(sheet, CostSegment.Options, Side.Sell, SellTurnover, after).Value;

        // 2026-03-31: schedule A
        buyA.ShouldBe(new LegCharges(Brokerage: 20.00m, Stt: 0.00m, ExchangeTxn: 2.35m, SebiFee: 0.01m, StampDuty: 0.23m, OtherCharges: 0m, DpCharge: 0m, Gst: 4.02m));
        buyA.Total.ShouldBe(26.61m);
        sellA.ShouldBe(new LegCharges(20.00m, 8.47m, 2.54m, 0.01m, 0m, 0m, 0m, 4.06m));
        sellA.Total.ShouldBe(35.08m);

        // 2026-04-01: schedule B
        buyB.ShouldBe(new LegCharges(20.00m, 0.00m, 2.74m, 0.01m, 0.23m, 0m, 0m, 4.10m));
        buyB.Total.ShouldBe(27.08m);
        sellB.ShouldBe(new LegCharges(20.00m, 12.70m, 2.96m, 0.01m, 0m, 0m, 0m, 4.13m));
        sellB.Total.ShouldBe(39.80m);
    }

    [Fact]
    public void A_sheet_without_option_lines_is_costs_unavailable_and_the_leg_is_gross_with_its_label_never_a_zero_line()
    {
        var gross = TestCostSheet.Sheet;

        var leg = CostModel.Leg(gross, CostSegment.Options, Side.Buy, BuyTurnover, new DateOnly(2026, 3, 31));
        var futures = CostModel.Leg(gross, CostSegment.Futures, Side.Sell, SellTurnover, new DateOnly(2026, 3, 31));
        var costed = OptionLegCost.Evaluate(gross, CostSegment.Options, Side.Buy, 120.50m, 65m, new DateOnly(2026, 3, 31), new HalfSpreadValue(HalfSpreadSource.Assumed, 0.5m, "spread: assumed 0.5 % by owner", null, false));

        leg.IsFailure.ShouldBeTrue();
        leg.Error.Code.ShouldBe(CostErrors.CostsUnavailableCode);
        futures.Error.Code.ShouldBe(CostErrors.CostsUnavailableCode);
        costed.Charges.ShouldBeNull();
        costed.ChargesReason.ShouldBe(CostErrors.CostsUnavailableCode);
        costed.ChargesLabel.ShouldBe("costs: none (no option lines)");
        costed.TurnoverRupees.ShouldBe(BuyTurnover);
        costed.SpreadRupees.ShouldBe(39.16m);
        costed.TotalRupees.ShouldBe(39.16m); // gross: no charges, the spread alone, and the label says so
    }

    [Fact]
    public void No_sheet_at_all_is_the_same_gross_answer_and_a_date_before_the_first_schedule_is_a_reason_not_a_charge()
    {
        var none = OptionLegCost.Evaluate(null, CostSegment.Options, Side.Sell, 130.25m, 65m, new DateOnly(2026, 4, 1), HalfSpreadValue.Unavailable);
        var early = OptionLegCost.Evaluate(OptionSheet(), CostSegment.Options, Side.Sell, 130.25m, 65m, new DateOnly(2020, 1, 1), HalfSpreadValue.Unavailable);

        none.Charges.ShouldBeNull();
        none.ChargesLabel.ShouldBe("costs: none (no option lines)");
        early.Charges.ShouldBeNull();
        early.ChargesReason.ShouldBe(CostErrors.NoRatesForDateCode);
        early.ChargesLabel.ShouldBe("costs: none (no schedule in force on the leg's date)");
        early.ChargesLabel.ShouldNotBe(none.ChargesLabel);
    }

    [Fact]
    public void The_spread_cost_is_the_half_spread_on_the_turnover_and_an_unavailable_spread_leaves_the_total_empty_never_zero()
    {
        var sheet = OptionSheet();
        var assumed = new HalfSpreadValue(HalfSpreadSource.Assumed, 0.5m, "spread: assumed 0.5 % by owner", null, false);

        var priced = OptionLegCost.Evaluate(sheet, CostSegment.Options, Side.Buy, 120.50m, 65m, new DateOnly(2026, 3, 31), assumed);
        var unpriced = OptionLegCost.Evaluate(sheet, CostSegment.Options, Side.Buy, 120.50m, 65m, new DateOnly(2026, 3, 31), HalfSpreadValue.Unavailable);

        priced.SpreadRupees.ShouldBe(39.16m); // 7,832.50 x 0.5 % = 39.1625
        priced.Charges!.Total.ShouldBe(26.61m);
        priced.TotalRupees.ShouldBe(65.77m);
        priced.ChargesLabel.ShouldBe("costs: sheet test-options-1 (TEST sheet: synthetic rates, not the broker's) (not checked against bills)");
        unpriced.SpreadRupees.ShouldBeNull();
        unpriced.TotalRupees.ShouldBeNull();
        unpriced.Charges!.Total.ShouldBe(26.61m);
    }

    [Fact]
    public void A_sheet_holds_fewer_schedules_with_both_new_segments_than_the_twelve_the_stored_text_bound_allows_shortcut()
    {
        ChargeSchedule Full(int month) => ChargeSchedule.Create(new DateOnly(2025, month, 1), 18m, ChargeRounding.HalfAwayFromZero, TestCostSheet.IntradayA, TestCostSheet.DeliveryA, OptionsA, OptionsB).Value;
        var twelve = CostSheet.Create("test-big", "big", "invented", null, [.. Enumerable.Range(1, 12).Select(Full)]);
        var six = CostSheet.Create("test-big", "big", "invented", null, [.. Enumerable.Range(1, 6).Select(Full)]);

        twelve.IsFailure.ShouldBeTrue();
        twelve.Error.Code.ShouldBe(CostErrors.SheetTooLongCode);
        six.IsSuccess.ShouldBeTrue();
        six.Value.ToCanonicalJson().Length.ShouldBeLessThanOrEqualTo(CostErrors.MaxCanonicalLength);
    }

    [Fact]
    public void The_cash_segments_are_not_costed_by_the_option_leg_and_an_unknown_segment_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OptionLegCost.Evaluate(OptionSheet(), CostSegment.Delivery, Side.Buy, 1m, 1m, OptionsFromA, HalfSpreadValue.Unavailable));
        Should.Throw<ArgumentOutOfRangeException>(() => CostModel.Leg(OptionSheet(), (CostSegment)9, Side.Buy, 1m, OptionsFromA));
    }

    [Fact]
    public void A_sheet_with_option_lines_writes_them_after_the_cash_lines_and_reads_them_back_with_a_hash_of_its_own()
    {
        var sheet = OptionSheet();

        var text = sheet.ToCanonicalJson();
        var parsed = CostSheetJson.Parse(text);

        text.ShouldContain("\"delivery_equity\":{");
        text.ShouldContain(",\"options\":{\"brokerage_pct\":0,\"brokerage_cap\":20,\"brokerage_min\":20,\"stt_buy_pct\":0,\"stt_sell_pct\":0.1,");
        text.ShouldNotContain("\"futures\"");
        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value.ShouldBe(sheet);
        parsed.Value.ToCanonicalJson().ShouldBe(text);
        parsed.Value.RatesSha256.ShouldBe(sheet.RatesSha256);
        sheet.Schedules[0].Options.ShouldBe(OptionsA);
        sheet.Schedules[0].Futures.ShouldBeNull();
        sheet.RatesSha256.ShouldNotBe(TestCostSheet.Sheet.RatesSha256);
    }

    [Fact]
    public void Futures_lines_are_charged_on_the_price_times_units_and_written_after_the_option_lines()
    {
        var futures = TestCostSheet.Rates(0m, 20m, 20m, 0m, 0.02m, 0.002m, 10m, 0.002m, 0m, 0m);
        var sheet = TestCostSheet.Of("test-futures-1", ChargeSchedule.Create(OptionsFromA, 18m, ChargeRounding.HalfAwayFromZero, TestCostSheet.IntradayA, TestCostSheet.DeliveryA, OptionsA, futures).Value);

        var sell = CostModel.Leg(sheet, CostSegment.Futures, Side.Sell, 1_000_000m, OptionsFromA).Value;

        sell.ShouldBe(new LegCharges(20.00m, 200.00m, 20.00m, 1.00m, 0m, 0m, 0m, 7.38m)); // STT 0.02 % = 200; exchange 0.002 % = 20; SEBI 10 per crore = 1; GST 18 % of 41 = 7.38
        sheet.ToCanonicalJson().IndexOf("\"options\"", StringComparison.Ordinal).ShouldBeLessThan(sheet.ToCanonicalJson().IndexOf("\"futures\"", StringComparison.Ordinal));
        CostSheetJson.Parse(sheet.ToCanonicalJson()).Value.ShouldBe(sheet);
        CostSheetJson.Parse(sheet.ToCanonicalJson()).Value.Schedules[0].Futures.ShouldBe(futures);
    }

    [Fact]
    public void The_option_lines_are_read_as_strictly_as_the_cash_lines_a_null_a_missing_line_or_an_unknown_name_is_refused()
    {
        var text = OptionSheet().ToCanonicalJson();

        var nullOptions = CostSheetJson.Parse(text.Replace("\"options\":{\"brokerage_pct\":0,\"brokerage_cap\":20,\"brokerage_min\":20,\"stt_buy_pct\":0,\"stt_sell_pct\":0.1,\"exchange_txn_pct\":0.03,\"sebi_per_crore\":10,\"stamp_buy_pct\":0.003,\"other_pct\":0,\"dp_charge_per_sell\":0}", "\"options\":null", StringComparison.Ordinal));
        var missingLine = CostSheetJson.Parse(text.Replace("\"stt_buy_pct\":0,\"stt_sell_pct\":0.1,", "\"stt_sell_pct\":0.1,", StringComparison.Ordinal));
        var unknown = CostSheetJson.Parse(text.Replace("\"options\":{", "\"options\":{\"margin_pct\":1,", StringComparison.Ordinal));
        var misspelt = CostSheetJson.Parse(text.Replace("\"options\":{", "\"option\":{", StringComparison.Ordinal));

        nullOptions.IsFailure.ShouldBeTrue();
        nullOptions.Error.Fields!.ShouldContain(f => f.Field == "schedules[0].options" && f.Code == CostErrors.InvalidValue);
        missingLine.Error.Fields!.ShouldContain(f => f.Field == "schedules[0].options.stt_buy_pct" && f.Code == CostErrors.Required);
        unknown.Error.Fields!.ShouldContain(f => f.Field == "schedules[0].options.margin_pct" && f.Code == CostErrors.UnknownField);
        misspelt.Error.Fields!.ShouldContain(f => f.Field == "schedules[0].option" && f.Code == CostErrors.UnknownField);
    }
}
