using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Domain.Tests.Market.Baskets.Lists;

public sealed class LotSizeParserTests
{
    private static readonly DateOnly Day = new(2026, 10, 26);

    private const string Header = "UNDERLYING      ,SYMBOL    ,OCT-26   ,NOV-26   ,DEC-26   ,";

    [Fact]
    public void Cells_are_trimmed_and_the_nearest_expiry_column_is_the_lot_size()
    {
        var file = LotSizeParser.Parse($"{Header}\r\nNIFTY BANK     ,BANKNIFTY ,30        ,30        ,30        ,\r\nRELIANCE INDUSTRIES ,RELIANCE  ,250       ,250       ,250       ,\r\n", Day);

        file.Ok.ShouldBeTrue();
        file.Rows.Count.ShouldBe(2);
        file.Rows[1].ShouldBe(new LotRow("RELIANCE INDUSTRIES", "RELIANCE", 250, "OCT-26"));
    }

    [Fact]
    public void After_the_month_end_the_next_column_is_nearest()
    {
        var file = LotSizeParser.Parse($"{Header}\nSAIL,SAIL,4700,4800,4900,\n", new DateOnly(2026, 11, 1));

        file.Rows.Single().ShouldBe(new LotRow("SAIL", "SAIL", 4800, "NOV-26"));
    }

    [Fact]
    public void On_the_last_day_of_the_month_that_expiry_is_still_the_nearest()
    {
        LotSizeParser.Parse($"{Header}\nSAIL,SAIL,4700,4800,4900,\n", new DateOnly(2026, 10, 31)).Rows.Single().LotSize.ShouldBe(4700);
    }

    [Fact]
    public void The_number_of_expiry_columns_may_vary()
    {
        var file = LotSizeParser.Parse("UNDERLYING,SYMBOL,OCT-26,NOV-26\nSAIL,SAIL,4700,4800\n", Day);

        file.Rows.Single().LotSize.ShouldBe(4700);
    }

    [Fact]
    public void A_row_with_no_lot_in_the_nearest_column_is_counted_not_listed()
    {
        var file = LotSizeParser.Parse($"{Header}\nSAIL,SAIL, - ,4800,4900,\nTATA,TATASTEEL,,4800,4900,\nHDFC,HDFCBANK,550,550,550,\n", Day);

        file.Rows.Select(r => r.Symbol).ShouldBe(["HDFCBANK"]);
        file.RowsWithoutLot.ShouldBe(2);
    }

    [Theory]
    [InlineData("SAIL,SAIL,many,4800,4900,")]
    [InlineData("SAIL,SAIL,0,4800,4900,")]
    [InlineData("SAIL,SAIL,-5,4800,4900,")]
    [InlineData("SAIL,SAIL,12.5,4800,4900,")]
    public void A_lot_that_is_not_a_positive_whole_number_rejects_the_file(string row)
    {
        LotSizeParser.Parse($"{Header}\n{row}\n", Day).Reason.ShouldBe(LotSizeParser.BadLot);
    }

    [Theory]
    [InlineData("<html>404</html>")]
    [InlineData("SYMBOL,UNDERLYING,OCT-26\nSAIL,SAIL,1")]
    [InlineData("UNDERLYING,SYMBOL,Q4\nSAIL,SAIL,1")]
    [InlineData("UNDERLYING,SYMBOL\nSAIL,SAIL")]
    public void A_wrong_header_is_rejected(string text)
    {
        LotSizeParser.Parse(text, Day).Reason.ShouldBe(LotSizeParser.BadHeader);
    }

    [Fact]
    public void An_empty_file_is_rejected()
    {
        LotSizeParser.Parse(" \r\n", Day).Reason.ShouldBe(LotSizeParser.Empty);
    }

    [Fact]
    public void A_file_whose_expiries_have_all_passed_is_rejected()
    {
        LotSizeParser.Parse("UNDERLYING,SYMBOL,JAN-26\nSAIL,SAIL,1\n", Day).Reason.ShouldBe(LotSizeParser.NoExpiry);
    }
}
