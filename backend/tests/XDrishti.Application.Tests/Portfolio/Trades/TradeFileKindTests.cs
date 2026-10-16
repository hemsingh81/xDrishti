using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Trades.TradeFiles;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>A file that is not a tradebook but is recognisably something else is answered as such (ADR 0028, P2-15 criterion 1): a dividend list, and a tradebook with no buy/sell column. Any other header keeps the missing-column answer.</summary>
public sealed class TradeFileKindTests
{
    private const string DividendHeader = "Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid";

    [Fact]
    public async Task A_dividend_list_dropped_on_the_trade_reader_is_a_wrong_kind_of_file_detected_as_dividends()
    {
        var result = await ReadRowsAsync(DividendHeader, "13 Jun 2024,Orion Steel,4,25,100.00");

        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Kind, result.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, ErrorKind.Validation, "dividends"));
        result.Error.Message.ShouldContain("dividend");
        result.Error.Message.ShouldContain("Import dividends");
    }

    [Fact]
    public async Task A_tradebook_with_order_type_and_segment_but_no_side_is_explained_plainly_not_as_a_missing_column()
    {
        var result = await ReadRowsAsync("Trade Date,Symbol,Order Type,Segment,Quantity,Price", "2024-03-05,EXMPL,Market,EQ,10,90");

        (result.Error.Code, result.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, "tradebook_without_sides"));
        result.Error.Message.ShouldContain("no buy/sell column");
        result.Error.Fields.ShouldBeNull();
    }

    [Theory]
    [InlineData("date,symbol,quantity")]
    [InlineData("foo,bar")]
    [InlineData("date,symbol,side,quantity")]
    public async Task Any_other_header_with_missing_columns_still_names_them(string header)
    {
        var result = await ReadRowsAsync(header, "1,2,3");

        (result.Error.Code, result.Error.Fields!.Single().Field).ShouldBe((PortfolioErrors.InvalidCode, "file"));
        result.Error.Fields!.Single().Message.ShouldContain("no column for");
    }

    [Fact]
    public async Task A_tradebook_with_a_side_column_is_read_as_before()
    {
        var file = await OkAsync(Header, "2024-03-05,EXMPL,INE000A01011,buy,10,90");

        file.Trades.ShouldHaveSingleItem().Qty.ShouldBe(10);
    }

    [Fact]
    public async Task The_trade_preview_answers_a_dividend_list_with_the_wrong_file_kind_and_stores_nothing()
    {
        var s = await TradeScenario.StartAsync();

        var result = await s.PreviewAsync($"{DividendHeader}\n13 Jun 2024,Orion Steel,4,25,100.00\n");

        (result.Error.Code, result.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, "dividends"));
        s.Data.Trades.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_trade_import_answers_a_dividend_list_the_same_way()
    {
        var s = await TradeScenario.StartAsync();

        var result = await s.ImportAsync($"{DividendHeader}\n13 Jun 2024,Orion Steel,4,25,100.00\n", rebuild: false);

        (result.Error.Code, result.Error.Payload!["detected"]).ShouldBe((PortfolioErrors.WrongFileKindCode, "dividends"));
        s.Data.Trades.ShouldBeEmpty();
    }
}
