using System.Text.Json;
using XDrishti.Application.Portfolio.Reports;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>The global transaction report reader (ADR 0029, P2-17 criteria 2 and 6) on a made-up file with the shape of Dhan's: title, personal lines, header, one row per bill, scrip and day, footer, note.</summary>
public sealed class GlobalTransactionReaderTests
{
    private const string Date = "07 Apr 2026 00:00:00";

    private static async Task<GtrFile> ReadAsync(params string[] rows) => Ok(GlobalTransactionReader.Read(await SheetAsync(Gtr(rows)), Today));

    private static async Task<GtrRow> OneAsync(string row)
    {
        var file = await ReadAsync(row);
        file.BadTotal.ShouldBe(0, string.Join("; ", file.Bad.Select(b => b.Reason)));
        return file.Items.ShouldHaveSingleItem();
    }

    private static async Task<string> BadReasonAsync(string row)
    {
        var file = await ReadAsync(row);
        file.Items.ShouldBeEmpty();
        return file.Bad.ShouldHaveSingleItem().Reason;
    }

    [Fact]
    public async Task The_made_up_sample_is_read_whole_with_its_period_client_code_and_footer()
    {
        var file = Ok(GlobalTransactionReader.Read(await SampleSheetAsync("global-transaction-report-shape.csv"), Today));

        file.Items.Count.ShouldBeGreaterThanOrEqualTo(40);
        file.BadTotal.ShouldBe(0);
        file.Repeated.ShouldBe(0);
        file.ClientCode.ShouldBe(ClientCode);
        file.From.ShouldBe(new DateOnly(2026, 4, 1));
        file.To.ShouldBe(new DateOnly(2026, 10, 15));
        file.FooterCharges.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_charges_of_all_rows_add_up_to_the_footers_total_charges_within_a_paisa()
    {
        var file = Ok(GlobalTransactionReader.Read(await SampleSheetAsync("global-transaction-report-shape.csv"), Today));

        Math.Abs(file.ChargesTotal - file.FooterCharges!.Value).ShouldBeLessThanOrEqualTo(0.01m);
        file.Items.Sum(i => i.Charges).ShouldBe(file.FooterCharges.Value);
    }

    [Fact]
    public async Task The_sample_has_every_case_the_reader_must_handle()
    {
        var file = Ok(GlobalTransactionReader.Read(await SampleSheetAsync("global-transaction-report-shape.csv"), Today));

        file.Items.ShouldContain(i => i.Name == "Orion Gold ETF (ORIONGLD)");
        file.Items.Select(i => i.Exchange).Distinct().Order().ShouldBe(["BSE", "MCX", "NSE"]);
        file.Items.ShouldContain(i => i.Name.StartsWith("FUT ", StringComparison.Ordinal));
        file.Items.ShouldContain(i => i.Name.StartsWith("OPT ", StringComparison.Ordinal));
        file.Items.ShouldContain(i => i.BuyQty > 0 && i.SellQty > 0);
        file.Items.ShouldContain(i => i.Charges != 0m && i.BuyQty == 0);
        file.Items.ShouldContain(i => i.Date.Month == 9);
        file.Items.GroupBy(i => i.Date).ShouldContain(g => g.Count() > 1);
        file.Items.Select(i => i.Name).Distinct().Count().ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task A_buy_only_a_sell_only_and_a_mixed_row_keep_their_quantities_and_values()
    {
        var buy = await OneAsync(GtrRow(Date, "Orion Gold ETF (ORIONGLD)", "NSE", "443400", 650, "15086.50", 0, "0.00", gst: "0.09", sebi: "0.02", stamp: "2.00", txn: "0.46"));
        var sell = await OneAsync(GtrRow(Date, "ORION STEEL LTD.", "NSE", "443401", 0, "0.00", 40, "148800.00", brokerage: "20.00", stt: "148.80"));
        var mixed = await OneAsync(GtrRow(Date, "LUMEN OILS LTD", "NSE", "443402", 100, "25000.00", 100, "25400.00"));

        (buy.BuyQty, buy.BuyValue, buy.SellQty, buy.SellValue).ShouldBe((650, 15086.50m, 0, 0m));
        buy.Charges.ShouldBe(2.57m);
        (sell.BuyQty, sell.SellQty, sell.SellValue, sell.Charges).ShouldBe((0, 40, 148800m, 168.80m));
        (mixed.BuyQty, mixed.SellQty).ShouldBe((100, 100));
    }

    [Theory]
    [InlineData("07 Apr 2026 00:00:00", 2026, 4, 7)]
    [InlineData("23 Sept 2026 00:00:00", 2026, 9, 23)]
    [InlineData("23 Sep 2026 09:15:00", 2026, 9, 23)]
    [InlineData("3 Jan 2026", 2026, 1, 3)]
    [InlineData("2026-04-07 00:00:00", 2026, 4, 7)]
    [InlineData("07-04-2026", 2026, 4, 7)]
    public async Task Dates_are_read_day_first_with_or_without_a_time_and_with_Sept_for_September(string text, int year, int month, int day)
    {
        var row = await OneAsync(GtrRow(text, "Orion Steel", "NSE", "1", 1, "10.00", 0, "0.00"));

        row.Date.ShouldBe(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("4,32,000.00", 432000)]
    [InlineData("1,234.50", 1234.5)]
    [InlineData("15086.50", 15086.5)]
    public async Task Values_with_thousands_separators_are_read_in_the_western_and_the_indian_grouping(string text, double expected)
    {
        var row = await OneAsync(GtrRow(Date, "Orion Steel", "NSE", "1", 10, text, 0, "0.00"));

        row.BuyValue.ShouldBe((decimal)expected);
    }

    [Fact]
    public async Task A_negative_charge_part_is_kept_in_the_rows_charges_and_a_negative_total_too()
    {
        var refund = await OneAsync(GtrRow(Date, "Orion Steel", "NSE", "1", 0, "0.00", 10, "1000.00", brokerage: "1.00", stt: "-0.50"));
        var net = await OneAsync(GtrRow(Date, "Orion Steel", "NSE", "2", 0, "0.00", 10, "1000.00", stt: "-0.50"));

        refund.Charges.ShouldBe(0.50m);
        net.Charges.ShouldBe(-0.50m);
    }

    [Fact]
    public async Task Empty_charge_cells_count_as_nothing()
    {
        var row = await OneAsync("\"" + string.Join("\",\"", Date, "Orion Steel", "NSE", "7", "5", "50.00", "0", "0.00", "", "", "", "", "", "", "", "-50.00") + "\"");

        row.Charges.ShouldBe(0m);
    }

    [Fact]
    public async Task The_footer_the_note_and_the_empty_lines_are_not_rows()
    {
        var file = await ReadAsync(GtrRow(Date, "Orion Steel", "NSE", "1", 1, "10.00", 0, "0.00"));

        file.Rows.ShouldBe(1);
        file.BadTotal.ShouldBe(0);
        file.FooterCharges.ShouldBe(0m);
    }

    [Fact]
    public async Task The_personal_lines_are_skipped_and_nothing_of_them_is_in_what_the_reader_returns_except_the_client_code()
    {
        var file = await ReadAsync(GtrRow(Date, "Orion Steel", "NSE", "1", 1, "10.00", 0, "0.00"));

        var text = JsonSerializer.Serialize(file);

        text.ShouldNotContain("SENTINEL");
        text.ShouldNotContain("example.invalid");
        file.ClientCode.ShouldBe(ClientCode);
    }

    [Fact]
    public async Task A_report_without_the_client_line_has_no_client_code()
    {
        var text = string.Join('\n', [GtrTitle, string.Empty, GtrHeader, GtrRow(Date, "Orion Steel", "NSE", "1", 1, "10.00", 0, "0.00")]) + "\n";

        var file = Ok(GlobalTransactionReader.Read(await SheetAsync(text), Today));

        file.ClientCode.ShouldBeNull();
        file.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_bill_that_appears_again_identically_is_a_repeat_and_with_other_details_a_bad_row()
    {
        var row = GtrRow(Date, "Orion Steel", "NSE", "77", 1, "10.00", 0, "0.00");
        var other = GtrRow(Date, "ORION STEEL LTD.", "NSE", "77", 2, "20.00", 0, "0.00");

        var file = await ReadAsync(row, row, other);

        file.Items.ShouldHaveSingleItem();
        file.Repeated.ShouldBe(1);
        file.BadTotal.ShouldBe(1);
        file.Bad.Single().Reason.ShouldContain("different details");
    }

    [Fact]
    public async Task The_same_bill_number_on_the_nse_and_on_the_bse_is_two_rows_not_a_bad_row()
    {
        var nse = GtrRow(Date, "Orion Steel", "NSE", "77", 1, "10.00", 0, "0.00");
        var bse = GtrRow(Date, "Orion Steel", "BSE", "77", 2, "20.00", 0, "0.00");

        var file = await ReadAsync(nse, bse);

        file.Items.Count.ShouldBe(2);
        (file.BadTotal, file.Repeated).ShouldBe((0, 0));
    }

    [Fact]
    public async Task The_same_bill_repeated_on_the_same_exchange_with_other_details_is_still_a_bad_row()
    {
        var nse = GtrRow(Date, "Orion Steel", "NSE", "77", 1, "10.00", 0, "0.00");
        var other = GtrRow(Date, "Orion Steel", " nse ", "77", 2, "20.00", 0, "0.00");

        var file = await ReadAsync(nse, other);

        (file.Items.Count, file.BadTotal).ShouldBe((1, 1));
    }

    [Fact]
    public async Task The_same_bill_number_for_another_scrip_or_another_day_is_another_row()
    {
        var file = await ReadAsync(
            GtrRow(Date, "Orion Steel", "NSE", "77", 1, "10.00", 0, "0.00"),
            GtrRow(Date, "Lumen Oils", "NSE", "77", 1, "10.00", 0, "0.00"),
            GtrRow("08 Apr 2026 00:00:00", "Orion Steel", "NSE", "77", 1, "10.00", 0, "0.00"));

        file.Items.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("31 Feb 2026 00:00:00", "is not a date")]
    [InlineData("01 Jan 2099 00:00:00", "future")]
    [InlineData("01 Jan 1970 00:00:00", "before")]
    public async Task A_date_that_is_not_one_or_is_out_of_range_makes_the_row_bad(string date, string reason)
    {
        var text = await BadReasonAsync(GtrRow(date, "Orion Steel", "NSE", "1", 1, "10.00", 0, "0.00"));

        text.ShouldContain(reason);
    }

    [Fact]
    public async Task A_row_with_no_quantity_no_name_or_no_bill_number_is_bad()
    {
        (await BadReasonAsync(GtrRow(Date, "Orion Steel", "NSE", "1", 0, "0.00", 0, "0.00"))).ShouldContain("neither a buy nor a sell");
        (await BadReasonAsync(GtrRow(Date, "", "NSE", "1", 1, "10.00", 0, "0.00"))).ShouldContain("scrip name");
        (await BadReasonAsync(GtrRow(Date, "Orion Steel", "NSE", "", 1, "10.00", 0, "0.00"))).ShouldContain("bill number");
    }

    [Theory]
    [InlineData(5, "0.00", "value above 0")]
    [InlineData(0, "10.00", "without a buy quantity")]
    [InlineData(5, "-10.00", "negative")]
    public async Task A_quantity_needs_a_value_and_a_value_needs_a_quantity(long quantity, string value, string reason)
    {
        var text = await BadReasonAsync(GtrRow(Date, "Orion Steel", "NSE", "1", quantity, value, 1, "5.00"));

        text.ShouldContain(reason);
    }

    [Fact]
    public async Task A_fractional_quantity_and_unreadable_numbers_are_bad_rows()
    {
        (await BadReasonAsync("\"" + string.Join("\",\"", Date, "Orion Steel", "NSE", "1", "1.5", "10.00", "0", "0.00", "0", "0", "0", "0", "0", "0", "0", "0") + "\"")).ShouldContain("whole number");
        (await BadReasonAsync(GtrRow(Date, "Orion Steel", "NSE", "1", 1, "ten", 0, "0.00"))).ShouldContain("not a number");
        (await BadReasonAsync(GtrRow(Date, "Orion Steel", "NSE", "1", 1, "10.00", 0, "0.00", gst: "x"))).ShouldContain("Charges");
    }

    [Fact]
    public async Task A_price_that_cannot_be_kept_makes_the_row_bad()
    {
        var text = await BadReasonAsync(GtrRow(Date, "Orion Steel", "NSE", "1", 1, "99999999999.00", 0, "0.00"));

        text.ShouldContain("price");
    }

    [Fact]
    public async Task A_sheet_without_the_header_is_refused_with_the_columns_named()
    {
        var result = GlobalTransactionReader.Read(await SheetAsync("Name,Someone\nUCC,1\n"), Today);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Field.ShouldBe("file");
        result.Error.Fields!.Single().Message.ShouldContain("Bill No.");
    }

    [Fact]
    public async Task A_report_missing_a_charge_column_is_refused_rather_than_summed_short()
    {
        var header = "Date,Scrip Name,Exchange,Bill No.,Buy Qty.,Buy Value,Sell Qty.,Sell Value,Brokerage,GST,STT,SEBI Fees,Stamp Duty,Txn. Charges,Gross Amount";

        var result = GlobalTransactionReader.Read(await SheetAsync(header + "\n"), Today);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Message.ShouldContain("Oth. Charges");
    }

    [Fact]
    public async Task Only_the_first_two_hundred_bad_rows_are_listed_and_all_are_counted()
    {
        var rows = Enumerable.Range(1, 230).Select(i => GtrRow("nonsense", "Orion Steel", "NSE", i.ToString(System.Globalization.CultureInfo.InvariantCulture), 1, "10.00", 0, "0.00"));

        var file = await ReadAsync([.. rows]);

        file.BadTotal.ShouldBe(230);
        file.Bad.Count.ShouldBe(200);
    }

    [Fact]
    public async Task A_title_that_spells_transaction_properly_is_read_too()
    {
        var text = Gtr([GtrRow(Date, "Orion Steel", "NSE", "1", 1, "10.00", 0, "0.00")]).Replace("transction", "transaction", StringComparison.Ordinal);

        var file = Ok(GlobalTransactionReader.Read(await SheetAsync(text), Today));

        file.From.ShouldBe(new DateOnly(2026, 4, 1));
    }
}
