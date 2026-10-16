using XDrishti.Application.Portfolio.Imports;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>The detector for Dhan's downloaded reports (ADR 0029, P2-17 criterion 1): by title line and header, in CSV, XLS and XLSX rows, and never fooled by the personal lines in front of the header.</summary>
public sealed class ReportDetectorTests
{
    private static FileKind Detect(params string[][] rows) => FileKindDetector.DetectSheet(rows);

    private static string[] Row(string line) => line.Split(',');

    [Fact]
    public void The_global_transaction_report_is_known_by_its_misspelt_title_line_and_by_its_header()
    {
        var titled = Detect(Row(ReportFiles.GtrTitle), Row("Name,Someone"), Row("UCC,1"), Row(ReportFiles.GtrHeader));
        var cutOff = Detect(Row("Mobile,0"), Row("Email ID,x@example.invalid"), [], Row(ReportFiles.GtrHeader));

        titled.ShouldBe(FileKind.GlobalTransactionReport);
        cutOff.ShouldBe(FileKind.GlobalTransactionReport);
        Detect(["Global transaction report", "From 01-04-2026 to 15-10-2026"]).ShouldBe(FileKind.GlobalTransactionReport);
    }

    [Fact]
    public void The_dp_charges_sheet_is_known_by_its_title_cell_and_by_its_header_row()
    {
        var titled = Detect(["DP Charges | From 01-April-2026 to 15-October-2026", "", "", "Name", "Someone"], ["Sr.", "Date", "ISIN"]);
        var headerOnly = Detect(["Name", "Someone", "UCC", "1"], ["Sr.", "Date", "ISIN", "Security Name", "", "", "Quantity", "Buy/Sell", "Type of Transaction", "", "Charges"]);

        titled.ShouldBe(FileKind.DpCharges);
        headerOnly.ShouldBe(FileKind.DpCharges);
    }

    [Fact]
    public void The_demat_holding_statement_is_known_by_its_title_and_header()
    {
        Detect(Row("Holding summary,For 15-10-2026"), Row("Name,Someone"), Row(ReportFiles.DematHeader)).ShouldBe(FileKind.DematHolding);
        Detect(Row("Name,Someone"), Row(ReportFiles.DematHeader)).ShouldBe(FileKind.DematHolding);
    }

    [Fact]
    public void Both_profit_and_loss_statements_are_recognised_as_pnl_reports()
    {
        Detect(Row("Realised PnL Report,From 01-04-2025 to 31-03-2026"), Row("Name,Someone"), Row("Scrip Name,Quantity,Avg. Buy Price,Buy Value,Avg. Sell Price,Sell Value,Realised P&L,Realised P&L %")).ShouldBe(FileKind.PnlReport);
        Detect(Row("PnL report,From 01-04-2025 to 31-03-2026"), Row("Name,Someone")).ShouldBe(FileKind.PnlReport);
        Detect(Row("Name,Someone"), Row("Scrip Name,Buy Qty.,Avg. Buy Price,Buy Value,Sell Qty.,Avg. Sell Price,Sell Value,Closing Price,Realised P&L,Realised P&L %,Unrealised P&L,Unrealised P&L %")).ShouldBe(FileKind.PnlReport);
    }

    [Fact]
    public void Tradebooks_and_dividend_lists_are_still_known_by_their_first_row()
    {
        Detect(Row("date,symbol,isin,side,quantity,price,trade id")).ShouldBe(FileKind.Trades);
        Detect(Row("Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid")).ShouldBe(FileKind.Dividends);
        Detect(Row("Trade Date,Symbol,Order Type,Segment,Quantity,Price")).ShouldBe(FileKind.TradebookWithoutSides);
        Detect(Row("Pay Date,Company,Amount")).ShouldBe(FileKind.Statement);
    }

    [Fact]
    public void A_file_with_personal_lines_first_and_no_known_header_is_unknown_and_an_empty_file_too()
    {
        Detect(Row("Name,Someone"), Row("UCC,1"), Row("Anything,Else")).ShouldBe(FileKind.Unknown);
        Detect().ShouldBe(FileKind.Unknown);
        Detect([""], ["", ""]).ShouldBe(FileKind.Unknown);
    }

    [Fact]
    public void A_header_that_looks_like_a_tradebook_but_sits_below_personal_lines_is_not_taken_for_one()
    {
        // Only the first row is judged as a tradebook or dividend header; below other lines the report headers are the only ones looked for.
        Detect(Row("Name,Someone"), Row("date,symbol,side,quantity,price")).ShouldBe(FileKind.Unknown);
    }

    [Theory]
    [InlineData("global_transaction_report", nameof(FileKind.GlobalTransactionReport))]
    [InlineData("dp_charges", nameof(FileKind.DpCharges))]
    [InlineData("demat_holding", nameof(FileKind.DematHolding))]
    [InlineData("pnl_report", nameof(FileKind.PnlReport))]
    [InlineData("trades", nameof(FileKind.Trades))]
    [InlineData("dividends", nameof(FileKind.Dividends))]
    [InlineData("unknown", nameof(FileKind.Unknown))]
    public void The_codes_of_the_kinds_are_the_ones_the_api_reports(string code, string kind)
    {
        FileKindDetector.Code(Enum.Parse<FileKind>(kind)).ShouldBe(code);
    }

    [Fact]
    public void The_reports_have_a_text_that_sends_the_owner_to_the_right_import()
    {
        FileKindDetector.IsReport(FileKind.GlobalTransactionReport).ShouldBeTrue();
        FileKindDetector.IsReport(FileKind.Trades).ShouldBeFalse();
        FileKindDetector.ReportElsewhereText(FileKind.DpCharges).ShouldContain("depository");
        FileKindDetector.ReportElsewhereText(FileKind.PnlReport).ShouldContain("not imported");
    }
}
