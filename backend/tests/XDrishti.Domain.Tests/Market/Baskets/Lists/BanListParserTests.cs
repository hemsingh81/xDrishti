using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Domain.Tests.Market.Baskets.Lists;

public sealed class BanListParserTests
{
    private const string First = "Securities in Ban For Trade Date 26-OCT-2026:";

    [Fact]
    public void Three_rows_are_three_symbols_dated_by_the_trade_date_of_the_first_line()
    {
        var file = BanListParser.Parse($"{First}\r\n1,AMBUJACEM\r\n2,KAYNES\r\n3,SAIL\r\n");

        file.Ok.ShouldBeTrue();
        file.TradeDate.ShouldBe(new DateOnly(2026, 10, 26));
        file.Symbols.ShouldBe(["AMBUJACEM", "KAYNES", "SAIL"]);
    }

    [Fact]
    public void The_first_line_only_is_a_valid_empty_list()
    {
        var file = BanListParser.Parse(First);

        (file.Ok, file.Symbols.Count, file.TradeDate).ShouldBe((true, 0, new DateOnly(2026, 10, 26)));
    }

    [Theory]
    [InlineData("Securities in Ban For Trade Date soon:\r\n1,SAIL")]
    [InlineData("Securities in Ban For Trade Date 31-FOO-2026:\r\n1,SAIL")]
    [InlineData("Securities in Ban For Trade Date :\r\n")]
    public void A_first_line_without_a_parseable_date_is_rejected(string text)
    {
        BanListParser.Parse(text).Reason.ShouldBe(BanListParser.BadDate);
    }

    [Theory]
    [InlineData("<html><body>404 Not Found</body></html>")]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("Symbol,Name\r\n1,SAIL")]
    public void Anything_that_does_not_start_like_the_ban_file_is_rejected(string text)
    {
        BanListParser.Parse(text).Reason.ShouldBe(BanListParser.BadHeader);
    }

    [Fact]
    public void A_BOM_blank_lines_padding_and_lower_case_are_tolerated()
    {
        var file = BanListParser.Parse($"﻿{First}\r\n\r\n 1 , sail \r\n\r\n2,KAYNES\r\n\r\n");

        file.Symbols.ShouldBe(["SAIL", "KAYNES"]);
    }

    [Fact]
    public void A_repeated_symbol_counts_once()
    {
        var file = BanListParser.Parse($"{First}\n1,SAIL\n2,SAIL\n");

        file.Symbols.ShouldBe(["SAIL"]);
        file.Duplicates.ShouldBe(1);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1,")]
    [InlineData("1,<b>SAIL</b>")]
    public void A_row_without_a_plausible_symbol_rejects_the_file(string row)
    {
        BanListParser.Parse($"{First}\n{row}\n").Reason.ShouldBe(BanListParser.BadRow);
    }

    [Fact]
    public void Symbols_with_an_ampersand_or_a_hyphen_are_valid()
    {
        BanListParser.Parse($"{First}\n1,M&M\n2,BAJAJ-AUTO\n").Symbols.ShouldBe(["M&M", "BAJAJ-AUTO"]);
    }
}
