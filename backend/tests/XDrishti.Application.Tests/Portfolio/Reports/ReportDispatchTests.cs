using XDrishti.Application.Portfolio.Reports;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>What the import screen's preview and import do with each kind of file (ADR 0029, P2-17 criteria 1 and 14): recognised by title and header, supported kinds imported, statements of results and files of another screen said so, unknown shapes refused with the accepted kinds.</summary>
public sealed class ReportDispatchTests
{
    private const string Pnl = "Realised PnL Report,From 01-04-2025 to 31-03-2026\nName,Someone\nUCC,1\n\nScrip Name,Quantity,Avg. Buy Price,Buy Value,Avg. Sell Price,Sell Value,Realised P&L,Realised P&L %\nExample Co,10,100.00,1000.00,120.00,1200.00,200.00,20.00\n";

    private const string PnlWithUnrealised = "PnL report,From 01-04-2025 to 31-03-2026\nName,Someone\n\nScrip Name,Buy Qty.,Avg. Buy Price,Buy Value,Sell Qty.,Avg. Sell Price,Sell Value,Closing Price,Realised P&L,Realised P&L %,Unrealised P&L,Unrealised P&L %\nExample Co,10,100,1000,5,120,600,130,100,20,150,30\n";

    private static Task<ReportScenario> StartAsync() => ReportScenario.StartAsync(s =>
    {
        s.Hold("EXMPL", "INE000A01011", 30, 100m);
        s.ListSampleCompanies();
    });

    [Theory]
    [InlineData(Pnl)]
    [InlineData(PnlWithUnrealised)]
    public async Task A_profit_and_loss_statement_is_recognised_and_said_to_be_a_statement_of_results_not_imported(string text)
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(text);
        var done = Ok(await s.ImportAsync(text, noDigest: true));

        (preview.Kind, preview.Supported).ShouldBe(("pnl_report", false));
        preview.Message!.ShouldContain("statement of results");
        preview.Warnings.Single().Code.ShouldBe("notice_only");
        (done.Status, done.Kind).ShouldBe(("not_supported", "pnl_report"));
        done.Message!.ShouldContain("not imported");
        s.Active().ShouldBeEmpty();
        s.Disclosed(preview, done).ShouldNotContain("Someone");
    }

    [Fact]
    public async Task A_dividend_list_is_sent_to_the_dividend_import()
    {
        var s = await StartAsync();
        var text = "Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid\n13 Jun 2024,Orion Steel Limited,4,25,100.00\n";

        var preview = await s.PreviewOkAsync(text);
        var done = Ok(await s.ImportAsync(text, noDigest: true));

        (preview.Kind, preview.Supported, preview.RouteTo).ShouldBe(("dividends", false, "dividends"));
        preview.Message!.ShouldContain("dividend");
        (done.Status, done.Kind).ShouldBe(("not_supported", "dividends"));
    }

    [Fact]
    public async Task A_tradebook_without_a_side_and_a_statement_of_amounts_are_said_not_to_be_importable()
    {
        var s = await StartAsync();

        var noSides = await s.PreviewOkAsync("Trade Date,Symbol,Order Type,Segment,Quantity,Price\n2026-04-01,EXMPL,x,EQ,1,10\n");
        var statement = await s.PreviewOkAsync("Pay Date,Company,Amount\n2026-04-01,Example,10\n");

        (noSides.Kind, noSides.Supported).ShouldBe(("tradebook_without_sides", false));
        noSides.Message!.ShouldContain("no buy/sell column");
        (statement.Kind, statement.Supported).ShouldBe(("statement", false));
    }

    [Fact]
    public async Task A_tradebook_with_a_buy_sell_column_is_previewed_and_imported_like_a_tradebook_import()
    {
        var s = await StartAsync();
        var text = "date,symbol,isin,side,quantity,price,trade id\n2024-03-05,EXMPL,INE000A01011,buy,10,90,T1\n2024-06-12,EXMPL,INE000A01011,buy,20,110,T2\n";

        var preview = await s.PreviewOkAsync(text);
        var done = await s.ImportOkAsync(text);

        (preview.Kind, preview.Supported).ShouldBe(("trades", true));
        (preview.Summary!.Rows, preview.Summary.ToAdd).ShouldBe((2, 2));
        preview.Trades!.Securities.Single().Symbol.ShouldBe("EXMPL");
        preview.Bill.ShouldBeNull();
        (done.Status, done.Trades!.Imported).ShouldBe(("imported", 2));
        s.Active().ShouldAllBe(t => t.Source == TradeSource.Csv);
    }

    [Fact]
    public async Task A_file_of_an_unknown_shape_is_refused_with_the_accepted_kinds()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync("Name,Someone\nFoo,Bar\n");
        var done = Ok(await s.ImportAsync("Name,Someone\nFoo,Bar\n", noDigest: true));

        (preview.Kind, preview.Supported).ShouldBe(("unknown", false));
        preview.Error!.Code.ShouldBe("portfolio.wrong_file_kind");
        preview.Error.Message.ShouldContain("global transaction report");
        preview.Error.Message.ShouldContain("DP charges");
        (done.Status, done.Error!.Code).ShouldBe(("rejected", "portfolio.wrong_file_kind"));
    }

    [Fact]
    public async Task A_file_that_is_not_text_or_a_spreadsheet_is_refused_on_the_file_field()
    {
        var s = await StartAsync();

        var preview = Ok(await new PreviewImportFileHandler(s.Workspace, s.Previewer, s.Fx.Clock, s.PreviewLog)
            .HandleAsync(new PreviewImportFileQuery(s.Account.Id, "x.bin", new XDrishti.Application.Portfolio.Trades.TradeFileContent([0xFF, 0xFE, 0xFD, 0x41]), []), CancellationToken.None));

        (preview.Kind, preview.Error!.Field).ShouldBe(("unknown", "file"));
    }

    [Fact]
    public async Task The_kind_of_a_report_is_judged_by_its_content_not_by_the_file_name()
    {
        var s = await StartAsync();
        var text = Gtr([GtrRow("07 Apr 2026 00:00:00", "ZENITH ENERGY SYSTEM LTD", "NSE", "1001", 100, "1000.00", 0, "0.00")]);

        var preview = Ok(await s.PreviewAsync(text, fileName: "tradebook.csv"));

        preview.Kind.ShouldBe("global_transaction_report");
    }

    [Fact]
    public async Task The_profit_and_loss_title_is_not_mistaken_for_a_report_header()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync("PnL report,From 01-04-2025 to 31-03-2026\nName,Someone\n");

        preview.Kind.ShouldBe("pnl_report");
    }

    [Fact]
    public async Task An_empty_cell_only_file_is_unknown()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(",,\n,,\n");

        preview.Kind.ShouldBe("unknown");
    }
}
