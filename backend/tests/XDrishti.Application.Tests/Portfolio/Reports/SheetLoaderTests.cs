using System.Text;
using XDrishti.Application.Portfolio.Reports;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>How a file becomes a sheet (ADR 0029): the format is judged from the first bytes, text is UTF-8 CSV, spreadsheets are read by the port.</summary>
public sealed class SheetLoaderTests
{
    private static readonly byte[] CompoundDocument = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0];

    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0];

    [Fact]
    public async Task Text_is_read_as_csv_with_quoted_fields_and_line_numbers()
    {
        var sheet = await SheetAsync("a,\"b,c\",d\n\n\"e\nf\",g\n");

        sheet.Rows.Count.ShouldBe(3);
        sheet.Rows[0].Cells.ShouldBe(["a", "b,c", "d"]);
        sheet.Rows[2].Line.ShouldBe(3);
        sheet.Rows[2].Cells[0].ShouldBe("e\nf");
        sheet.Rows[1].IsBlank.ShouldBeTrue();
    }

    [Fact]
    public async Task A_byte_order_mark_is_skipped()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Date,Scrip Name\n")).ToArray();

        var sheet = Ok(await SheetLoader.LoadAsync(new XDrishti.Application.Portfolio.Trades.TradeFileContent(bytes), new FakeSpreadsheets(), CancellationToken.None));

        sheet.Rows[0].Cells[0].ShouldBe("Date");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_legacy_spreadsheet_and_an_xlsx_archive_are_read_by_the_spreadsheet_port_whatever_the_file_is_called(int kind)
    {
        var spreadsheets = new FakeSpreadsheets { Rows = [["Date", "ISIN"], ["x", "y"]] };
        var bytes = kind == 0 ? CompoundDocument : Zip;

        var sheet = Ok(await SheetLoader.LoadAsync(new XDrishti.Application.Portfolio.Trades.TradeFileContent(bytes), spreadsheets, CancellationToken.None));

        spreadsheets.Reads.ShouldBe(1);
        sheet.Rows.Count.ShouldBe(2);
        sheet.Rows[1].Line.ShouldBe(2);
    }

    [Fact]
    public async Task Text_never_reaches_the_spreadsheet_port()
    {
        var spreadsheets = new FakeSpreadsheets();

        await SheetLoader.LoadAsync(Content("a,b\n"), spreadsheets, CancellationToken.None);

        spreadsheets.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Bytes_that_are_not_utf8_text_are_refused_with_a_message_on_the_file_field()
    {
        var result = await SheetLoader.LoadAsync(new XDrishti.Application.Portfolio.Trades.TradeFileContent([0xFF, 0xFE, 0xFD, 0x41, 0x0A]), new FakeSpreadsheets(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Field.ShouldBe("file");
        result.Error.Fields!.Single().Message.ShouldContain("UTF-8");
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        var result = await SheetLoader.LoadAsync(new XDrishti.Application.Portfolio.Trades.TradeFileContent([]), new FakeSpreadsheets(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task More_rows_than_the_limit_are_refused()
    {
        var text = string.Join('\n', Enumerable.Repeat("a,b", ReportLimits.MaxRows + 1));

        var result = await SheetLoader.LoadAsync(Content(text), new FakeSpreadsheets(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Message.ShouldContain("data rows");
    }
}
