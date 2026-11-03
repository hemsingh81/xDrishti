using XDrishti.Application.Market.Eod;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Tests.Market.Eod;

/// <summary>CF-04 criterion 13: both formats map to the same logical row, the delivery arithmetic, the adjusted close, and an unknown header refuses the file.</summary>
public sealed class EodParserTests
{
    private static readonly string[] Eq = ["EQ"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_formats_map_to_the_same_logical_row()
    {
        var legacy = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ")), EodFixtures.Day, Eq, Ct);
        var udiff = await EodParser.ParseBhavcopyAsync(EodFixtures.Udiff(EodFixtures.UdiffRow("ABC", "EQ")), EodFixtures.Day, Eq, Ct);

        legacy.IsSuccess.ShouldBeTrue();
        udiff.IsSuccess.ShouldBeTrue();
        legacy.Value.Layout.ShouldBe("legacy");
        udiff.Value.Layout.ShouldBe("udiff");
        udiff.Value.Rows.ShouldBe(legacy.Value.Rows);
        legacy.Value.Rows.Single().ShouldSatisfyAllConditions(
            r => r.Symbol.ShouldBe("ABC"),
            r => r.Close.ShouldBe(100.50m),
            r => r.PrevClose.ShouldBe(99.50m),
            r => r.TradedQty.ShouldBe(10_000),
            r => r.Turnover.ShouldBe(1005000.00m),
            r => r.Isin.ShouldBe("INE000A01010"));
    }

    [Fact]
    public async Task Only_the_wanted_series_are_kept_and_the_others_are_counted()
    {
        var result = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ"), EodFixtures.LegacyRow("XYZ", "BE")), EodFixtures.Day, Eq, Ct);

        result.Value.Rows.Select(r => r.Symbol).ShouldBe(["ABC"]);
        result.Value.OtherSeries.ShouldBe(1);
    }

    [Fact]
    public async Task A_row_with_an_unreadable_number_is_counted_and_skipped()
    {
        var good = Enumerable.Range(0, 20).Select(i => EodFixtures.LegacyRow("S" + i, "EQ"));
        var result = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy([.. good, EodFixtures.LegacyRow("BAD", "EQ", close: "n/a")]), EodFixtures.Day, Eq, Ct);

        result.Value.Rows.Count.ShouldBe(20);
        result.Value.Rejected.ShouldBe(1);
    }

    [Fact]
    public async Task A_known_header_where_no_row_can_be_read_is_refused_not_loaded_as_an_empty_day()
    {
        var result = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("A", "EQ", close: "n/a"), EodFixtures.LegacyRow("B", "EQ", close: "n/a")), EodFixtures.Day, Eq, Ct);

        result.Error.Code.ShouldBe("history.unknown_format");
    }

    [Fact]
    public async Task A_file_with_too_many_unreadable_rows_is_refused_whole()
    {
        var good = Enumerable.Range(0, 10).Select(i => EodFixtures.LegacyRow("S" + i, "EQ"));
        var result = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy([.. good, EodFixtures.LegacyRow("BAD", "EQ", close: "n/a")]), EodFixtures.Day, Eq, Ct); // 1 of 11 is above the 5 % share

        result.Error.Code.ShouldBe("history.unknown_format");
    }

    [Fact]
    public async Task The_same_symbol_and_series_twice_in_one_file_refuses_the_file_with_a_named_code()
    {
        var result = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ"), EodFixtures.LegacyRow("ABC", "EQ", close: "101.00")), EodFixtures.Day, Eq, Ct);

        result.Error.Code.ShouldBe("history.duplicate_row");
    }

    [Fact]
    public async Task A_delivery_figure_on_a_day_of_nothing_traded_leaves_both_delivery_fields_null()
    {
        var bhav = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ", traded: 0)), EodFixtures.Day, Eq, Ct);
        var delivery = await EodParser.ParseDeliveryAsync(EodFixtures.Delivery("ABC,EQ,0,0,0.00"), Ct);

        var merged = EodParser.Merge(bhav.Value.Rows, delivery.Value);

        merged.Rows.Single().ShouldSatisfyAllConditions(r => r.DeliveryQty.ShouldBeNull(), r => r.DeliveryPct.ShouldBeNull());
        merged.Rejected.ShouldBe(0);
    }

    [Theory]
    [InlineData("SYMBOL,SERIES,OPEN,HIGH,LOW,CLOSE,PREVCLOSE,TOTTRDQTY,TOTTRDVAL,TIMESTAMP")] // legacy without the traded value renamed away
    [InlineData("Symbol,Series,Px")]
    [InlineData("")]
    public async Task An_unknown_header_refuses_the_whole_file(string header)
    {
        var header2 = header.Replace("TOTTRDVAL,", "TURNOVER,", StringComparison.Ordinal);
        var bytes = System.Text.Encoding.UTF8.GetBytes(header2 + "\nABC,EQ,1,1,1,1,1,10,10,05-JUL-2024\n");

        var result = await EodParser.ParseBhavcopyAsync(bytes, EodFixtures.Day, Eq, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("history.unknown_format");
    }

    [Fact]
    public async Task A_row_of_another_date_refuses_the_whole_file()
    {
        var result = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ"), EodFixtures.LegacyRow("DEF", "EQ", date: "04-JUL-2024")), EodFixtures.Day, Eq, Ct);

        result.Error.Code.ShouldBe("history.date_mismatch");
    }

    [Fact]
    public async Task Delivery_of_6000_in_10000_is_sixty_percent()
    {
        var bhav = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ")), EodFixtures.Day, Eq, Ct);
        var delivery = await EodParser.ParseDeliveryAsync(EodFixtures.Delivery("ABC,EQ,10000,6000,60.00"), Ct);

        var merged = EodParser.Merge(bhav.Value.Rows, delivery.Value);

        merged.Merged.ShouldBe(1);
        merged.Rows.Single().DeliveryQty.ShouldBe(6000);
        merged.Rows.Single().DeliveryPct.ShouldBe(60.00m);
    }

    [Fact]
    public async Task Delivery_above_the_traded_quantity_is_rejected_and_the_price_row_stays_without_delivery()
    {
        var bhav = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ")), EodFixtures.Day, Eq, Ct);
        var delivery = await EodParser.ParseDeliveryAsync(EodFixtures.Delivery("ABC,EQ,10000,10001,100.01"), Ct);

        var merged = EodParser.Merge(bhav.Value.Rows, delivery.Value);

        merged.Rejected.ShouldBe(1);
        merged.Rows.Single().DeliveryQty.ShouldBeNull();
    }

    [Fact]
    public async Task A_file_percent_more_than_one_hundredth_of_a_point_off_the_recomputed_one_is_rejected()
    {
        var bhav = await EodParser.ParseBhavcopyAsync(EodFixtures.Legacy(EodFixtures.LegacyRow("ABC", "EQ")), EodFixtures.Day, Eq, Ct);
        var delivery = await EodParser.ParseDeliveryAsync(EodFixtures.Delivery("ABC,EQ,10000,6000,60.05"), Ct);

        EodParser.Merge(bhav.Value.Rows, delivery.Value).Rejected.ShouldBe(1);
    }

    [Fact]
    public async Task A_delivery_file_without_the_known_header_is_refused()
    {
        var result = await EodParser.ParseDeliveryAsync(System.Text.Encoding.UTF8.GetBytes("a,b,c\n1,2,3\n"), Ct);

        result.Error.Code.ShouldBe("history.unknown_format");
    }

    [Fact]
    public void A_raw_close_of_1000_before_a_split_1_to_5_reads_200_adjusted()
    {
        var factors = AdjustmentFactors.From([new AppliedAction(new DateOnly(2024, 8, 1), CorporateActionKind.Split, 1, 5, 0.2m)]);

        AdjustmentFactors.Price(1000.00, factors.FactorAt(new DateOnly(2024, 7, 5))).ShouldBe(200.00);
        AdjustmentFactors.Price(1000.00, factors.FactorAt(new DateOnly(2024, 8, 1))).ShouldBe(1000.00);
    }

    [Theory]
    [InlineData(10_000, 6_000, 60.00)]
    [InlineData(3, 1, 33.33)]
    public void The_percent_is_two_decimals_from_the_quantities(long traded, long delivered, double expected) =>
        EodParser.Percent(delivered, traded).ShouldBe((decimal)expected);
}
