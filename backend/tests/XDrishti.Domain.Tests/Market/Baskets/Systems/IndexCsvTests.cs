using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Domain.Tests.Market.Baskets.Systems;

public sealed class IndexCsvTests
{
    private const string Header = "Company Name,Industry,Symbol,Series,ISIN Code";

    [Fact]
    public void A_file_with_a_bom_crlf_and_blank_lines_is_read()
    {
        var text = "﻿" + Header + "\r\n\r\nReliance Industries Ltd.,Oil Gas & Consumable Fuels,RELIANCE,EQ,INE002A01018\r\n\r\nTCS Ltd.,IT,tcs,EQ,ine467b01029\r\n\r\n";

        var file = IndexCsv.Parse(text);

        file.HeaderOk.ShouldBeTrue();
        file.Rows.Select(r => (r.Symbol, r.Isin)).ShouldBe([("RELIANCE", "INE002A01018"), ("TCS", "INE467B01029")]);
    }

    [Fact]
    public void A_repeated_symbol_row_is_left_out_and_counted()
    {
        var text = Header + "\nA Ltd,IT,AAA,EQ,INE000A01011\nA Ltd,IT,AAA,EQ,INE000A01011\nB Ltd,IT,BBB,EQ,INE000B01011\n";

        var file = IndexCsv.Parse(text);

        (file.Rows.Count, file.DuplicateRows).ShouldBe((2, 1));
    }

    [Fact]
    public void A_quoted_company_name_with_a_comma_keeps_its_columns()
    {
        var file = IndexCsv.Parse(Header + "\n\"Tata Motors, Ltd.\",Auto,TATAMOTORS,EQ,INE155A01022\n");

        file.Rows.Single().Symbol.ShouldBe("TATAMOTORS");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>Not Found</body></html>")]
    [InlineData("Company Name,Industry,Symbol,Series\nA,B,C,D")]
    [InlineData("Company Name,Industry,Symbol,Series,ISIN Code,Extra\nA,B,C,D,E,F")]
    public void Another_header_is_not_ok(string text)
    {
        IndexCsv.Parse(text).HeaderOk.ShouldBeFalse();
    }
}
