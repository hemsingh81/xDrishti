using XDrishti.Application.Portfolio.Imports;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>The file-kind detector (ADR 0028, P2-15 criterion 1): dividends, trades, a tradebook without sides, or unknown — from the header alone.</summary>
public sealed class FileKindDetectorTests
{
    private static FileKind Detect(string header) => FileKindDetector.Detect(header.Split(','));

    [Theory]
    [InlineData("Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid")]
    [InlineData("date,scrip_name,dividend_per_share,qty,dividend_paid")]
    [InlineData("Pay Date,Company,Dividend Per Share,Amount")]
    [InlineData("Date,Symbol,DPS,Net Amount")]
    [InlineData("Date,Symbol,Total Amount,Dividend")]
    [InlineData("Date,ISIN,Payout")]
    [InlineData("Date,Name,DPS,Shares,Rate,Dividend Received")]
    public void A_date_a_name_and_a_paid_amount_make_a_dividend_file(string header)
    {
        Detect(header).ShouldBe(FileKind.Dividends);
    }

    [Theory]
    [InlineData("date,symbol,isin,side,quantity,price")]
    [InlineData("Trade Date,Symbol,Trade Type,Quantity,Price,Exchange")]
    [InlineData("Date,Scrip,Buy/Sell,Qty,Rate,Amount")]
    [InlineData("Date,Symbol,Side,Quantity,Price,Net Amount")]
    public void A_tradebook_with_a_side_column_is_trades_even_when_it_has_an_amount(string header)
    {
        Detect(header).ShouldBe(FileKind.Trades);
    }

    [Theory]
    [InlineData("Trade Date,Symbol,Order Type,Segment,Quantity,Price")]
    [InlineData("Date,Scrip,Qty,Price,Segment,Amount")]
    [InlineData("Date,Symbol,Qty,Rate")]
    public void A_tradebook_with_no_side_column_is_a_tradebook_without_sides_unless_an_amount_says_dividend(string header)
    {
        Detect(header).ShouldBe(FileKind.TradebookWithoutSides);
    }

    [Theory]
    [InlineData("Pay Date,Company,Amount")]
    [InlineData("Date,Symbol,Net Amount")]
    [InlineData("Date,Name,Total Amount")]
    [InlineData("Date,ISIN,Amount,Remarks")]
    public void A_date_a_name_and_a_plain_amount_with_nothing_that_says_dividend_make_a_statement_not_a_dividend_file(string header)
    {
        Detect(header).ShouldBe(FileKind.Statement);
    }

    [Theory]
    [InlineData("Date,Symbol,Quantity,Price,Net Amount")]
    [InlineData("Date,Symbol,Qty,Rate,Amount")]
    public void A_tradebook_with_a_plain_amount_and_no_side_is_a_tradebook_without_sides_never_a_dividend_file(string header)
    {
        Detect(header).ShouldBe(FileKind.TradebookWithoutSides);
    }

    [Fact]
    public void A_header_with_a_dividend_amount_and_a_rate_column_is_still_a_dividend_file()
    {
        Detect("Date,Scrip,Qty,Rate,Dividend Paid").ShouldBe(FileKind.Dividends);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Foo,Bar,Baz")]
    [InlineData("Date,Amount")]
    [InlineData("Symbol,Quantity")]
    [InlineData("Date,Symbol,Side,Amount")]
    [InlineData("Date,Symbol,Side,Quantity,Net Amount")]
    public void Anything_else_is_unknown(string header)
    {
        Detect(header).ShouldBe(FileKind.Unknown);
    }

    [Fact]
    public void The_codes_are_the_names_the_api_reports()
    {
        FileKindDetector.Code(FileKind.Dividends).ShouldBe("dividends");
        FileKindDetector.Code(FileKind.Trades).ShouldBe("trades");
        FileKindDetector.Code(FileKind.TradebookWithoutSides).ShouldBe("tradebook_without_sides");
        FileKindDetector.Code(FileKind.Statement).ShouldBe("statement");
        FileKindDetector.Code(FileKind.Unknown).ShouldBe("unknown");
    }
}
