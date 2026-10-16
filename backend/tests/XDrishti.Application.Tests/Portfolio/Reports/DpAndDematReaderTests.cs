using System.Text.Json;
using XDrishti.Application.Portfolio.Reports;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>The depository-charges and the demat-holding readers (ADR 0029, P2-17 criteria 3 and 4) on made-up sheets with the shape of Dhan's.</summary>
public sealed class DpAndDematReaderTests
{
    private static async Task<DpFile> DpAsync(params string[] lines) => Ok(DpChargesReader.Read(await SheetAsync(Dp(lines)), Today));

    private static async Task<DematFile> DematAsync(params string[] rows) => Ok(DematHoldingReader.Read(await SheetAsync(Demat(rows))));

    [Fact]
    public async Task A_sell_line_with_leading_spaces_is_read_with_its_isin_quantity_side_type_and_charge()
    {
        var file = await DpAsync(DpLine(1, "04-Jun-2026", "INE245A01021", "THE EXAMPLE POWER COMPANY LIMITED", " 11.000", " Sell", " Other Fees", " 12.5"));

        var line = file.Lines.ShouldHaveSingleItem();
        line.Date.ShouldBe(new DateOnly(2026, 6, 4));
        line.Isin.ShouldBe("INE245A01021");
        line.Name.ShouldBe("THE EXAMPLE POWER COMPANY LIMITED");
        line.Quantity.ShouldBe(11);
        line.Side.ShouldBe(DpSide.Sell);
        line.Type.ShouldBe("Other Fees");
        line.Charge.ShouldBe(12.5m);
    }

    [Fact]
    public async Task The_total_row_the_signature_row_and_the_generated_on_row_are_not_lines()
    {
        var file = await DpAsync(DpLine(1, "04-Jun-2026", "INE245A01021", "Example Power", "11.000", "Sell", "Other Fees", "12.5"));

        file.Lines.Count.ShouldBe(1);
        file.Rows.ShouldBe(1);
        file.BadTotal.ShouldBe(0);
    }

    [Fact]
    public async Task Zero_charges_buy_side_lines_and_off_market_lines_are_classified_by_what_the_sheet_says()
    {
        var file = await DpAsync(
            DpLine(1, "03-Jun-2026", "INE245A01021", "Example Power", "11.000", "Buy", "DP Fees", "0"),
            DpLine(2, "04-Jun-2026", "INE245A01021", "Example Power", "11.000", "Sell", "DP Fees", "0"),
            DpLine(3, "05-Jun-2026", "INE245A01021", "Example Power", "5.000", "Sell", "Off-Market Fees", "25"),
            DpLine(4, "06-Jun-2026", "INE245A01021", "Example Power", "", "", "Other Fees", ""));

        file.Lines.Select(l => (l.Side, l.Charge)).ShouldBe([(DpSide.Buy, 0m), (DpSide.Sell, 0m), (DpSide.Sell, 25m), (DpSide.Other, 0m)]);
        file.Lines.Select(l => l.Type).ShouldBe(["DP Fees", "DP Fees", "Off-Market Fees", "Other Fees"]);
        file.Lines[3].Quantity.ShouldBe(0);
    }

    [Fact]
    public async Task The_title_gives_the_period_and_the_client_code_is_read_for_a_comparison_only()
    {
        var file = await DpAsync(DpLine(1, "04-Jun-2026", "INE245A01021", "Example Power", "11", "Sell", "Other Fees", "12.5"));

        file.From.ShouldBe(new DateOnly(2026, 4, 1));
        file.To.ShouldBe(new DateOnly(2026, 10, 15));
        file.ClientCode.ShouldBe(ClientCode);
        JsonSerializer.Serialize(file).ShouldNotContain("SENTINEL");
    }

    [Theory]
    [InlineData("31-Feb-2026", "11", "12.5", "INE245A01021", "is not a date")]
    [InlineData("04-Jun-2026", "11", "12.5", "NOTANISIN", "not an ISIN")]
    [InlineData("04-Jun-2026", "1.5", "12.5", "INE245A01021", "whole number")]
    [InlineData("04-Jun-2026", "11", "-3", "INE245A01021", "between 0 and")]
    [InlineData("04-Jun-2026", "11", "abc", "INE245A01021", "not a number")]
    [InlineData("04-Jun-2099", "11", "12.5", "INE245A01021", "between")]
    public async Task A_line_with_something_wrong_is_bad_with_its_line_and_reason(string date, string quantity, string charge, string isin, string reason)
    {
        var file = await DpAsync(DpLine(1, date, isin, "Example Power", quantity, "Sell", "Other Fees", charge));

        file.Lines.ShouldBeEmpty();
        file.BadTotal.ShouldBe(1);
        file.Bad.Single().Reason.ShouldContain(reason);
    }

    [Fact]
    public async Task A_sheet_without_the_header_is_refused()
    {
        var result = DpChargesReader.Read(await SheetAsync("DP Charges | From 01-April-2026 to 15-October-2026\nNothing,here\n"), Today);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Message.ShouldContain("Type of Transaction");
    }

    [Fact]
    public void A_spreadsheet_row_set_is_read_like_a_csv_one_with_dates_and_numbers_as_the_reader_writes_them()
    {
        var sheet = new Sheet(
        [
            new SheetRow(1, ["DP Charges | From 01-April-2026 to 15-October-2026", "", "", "Name", "ZZ-SENTINEL-NAME"]),
            new SheetRow(2, ["Sr.", "Date", "ISIN", "Security Name", "", "", "", "Quantity", "Buy/Sell", "Type of Transaction", "", "Charges"]),
            new SheetRow(3, ["1", "2026-06-04", "INE245A01021", "  EXAMPLE POWER  ", "", "", "", "11", "Sell", "Other Fees", "", "12.5"]),
            new SheetRow(4, ["Total", "", "", "", "", "", "", "", "", "", "", "12.5"]),
        ]);

        var file = Ok(DpChargesReader.Read(sheet, Today));

        var line = file.Lines.ShouldHaveSingleItem();
        (line.Date, line.Name, line.Quantity, line.Charge).ShouldBe((new DateOnly(2026, 6, 4), "EXAMPLE POWER", 11L, 12.5m));
    }

    [Fact]
    public async Task The_depository_quantity_is_free_plus_locked_in_plus_safe_keep_plus_every_pledge()
    {
        var file = await DematAsync(DematRow("Example Power", "INE245A01021", free: "10", locked: "5", safeKeep: "3", mtf: "2", margin: "1", cusa: "4"));

        var row = file.Items.ShouldHaveSingleItem();
        row.Quantity.ShouldBe(25m);
        row.Isin.ShouldBe("INE245A01021");
        row.ClosingPrice.ShouldBe(100m);
    }

    [Fact]
    public async Task The_valuation_and_count_footer_the_title_date_and_the_personal_lines_are_handled()
    {
        var file = await DematAsync(DematRow("Example Power", "INE245A01021", free: "\"1,000\""));

        file.Rows.ShouldBe(1);
        file.AsOf.ShouldBe(new DateOnly(2026, 10, 15));
        file.ClientCode.ShouldBe(ClientCode);
        file.Items.Single().Quantity.ShouldBe(1000m);
        JsonSerializer.Serialize(file).ShouldNotContain("SENTINEL");
    }

    [Theory]
    [InlineData("", "INE245A01021", "10", "scrip name")]
    [InlineData("Example", "BAD", "10", "not an ISIN")]
    [InlineData("Example", "INE245A01021", "-1", "negative")]
    [InlineData("Example", "INE245A01021", "x", "not a number")]
    public async Task A_row_with_something_wrong_is_bad(string name, string isin, string free, string reason)
    {
        var file = await DematAsync(DematRow(name, isin, free));

        file.Items.ShouldBeEmpty();
        file.Bad.Single().Reason.ShouldContain(reason);
    }

    [Fact]
    public async Task A_statement_without_the_header_is_refused()
    {
        DematHoldingReader.Read(await SheetAsync("Holding summary,For 15-10-2026\nName,x\n")).IsFailure.ShouldBeTrue();
    }
}
