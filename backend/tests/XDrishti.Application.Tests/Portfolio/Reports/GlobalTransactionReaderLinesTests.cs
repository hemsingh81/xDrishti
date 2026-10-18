using XDrishti.Application.Portfolio.Reports;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>Criterion 9 of P3-03f: the reader keeps the seven charge cells of every row by column, <c>Charges</c> stays the rounded sum, and the importer's planned trades do not change.</summary>
public sealed class GlobalTransactionReaderLinesTests
{
    private const string Date = "07 Apr 2026 00:00:00";

    [Fact]
    public async Task Every_row_of_the_shape_sample_carries_its_seven_cells_and_their_rounded_sum_is_the_charges()
    {
        var file = Ok(GlobalTransactionReader.Read(await SampleSheetAsync("global-transaction-report-shape.csv"), Today));

        file.Items.ShouldNotBeEmpty();
        foreach (var row in file.Items)
        {
            var lines = row.Lines.ShouldNotBeNull();
            TradeRules.Round(lines.Brokerage + lines.Gst + lines.Stt + lines.SebiFees + lines.StampDuty + lines.TxnCharges + lines.OtherCharges).ShouldBe(row.Charges, $"line {row.Line}");
        }
    }

    [Fact]
    public async Task A_negative_other_cell_a_blank_cell_and_a_thousands_separator_are_kept_by_column()
    {
        var text = Gtr([GtrRow(Date, "ZZ SCRIP ONE", "NSE", "ZZBILL01", 100, "10000.00", 0, "0.00", brokerage: "1,234.50", gst: "0.08", stt: string.Empty, sebi: "0.05", stamp: "3.00", txn: "0.40", other: "-0.01")]);

        var file = Ok(GlobalTransactionReader.Read(await SheetAsync(text), Today));

        var row = file.Items.ShouldHaveSingleItem();
        row.Lines.ShouldBe(new GtrLines(1234.50m, 0.08m, 0m, 0.05m, 3.00m, 0.40m, -0.01m));
        row.Charges.ShouldBe(1238.02m, "the sum of the seven cells, rounded: unchanged by the extension");
    }

    [Fact]
    public async Task The_columns_are_found_by_name_whatever_their_order()
    {
        var header = "Date,Scrip Name,Exchange,Bill No.,Buy Qty.,Buy Value,Sell Qty.,Sell Value,Oth. Charges,Txn. Charges,Stamp Duty,SEBI Fees,STT,GST,Brokerage,Gross Amount";
        var row = "07 Apr 2026 00:00:00,ZZ SCRIP ONE,NSE,ZZBILL01,100,10000.00,0,0.00,7.00,6.00,5.00,4.00,3.00,2.00,1.00,0.00";
        var text = string.Join('\n', [GtrTitle, "UCC," + ClientCode, string.Empty, header, row]) + "\n";

        var file = Ok(GlobalTransactionReader.Read(await SheetAsync(text), Today));

        file.Items.ShouldHaveSingleItem().Lines.ShouldBe(new GtrLines(Brokerage: 1m, Gst: 2m, Stt: 3m, SebiFees: 4m, StampDuty: 5m, TxnCharges: 6m, OtherCharges: 7m));
    }

    [Fact]
    public async Task A_report_missing_one_charge_column_is_still_refused_as_before()
    {
        var header = GtrHeader.Replace("Txn. Charges,", string.Empty, StringComparison.Ordinal);
        var text = string.Join('\n', [GtrTitle, "UCC," + ClientCode, string.Empty, header]) + "\n";

        var result = GlobalTransactionReader.Read(await SheetAsync(text), Today);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields.ShouldNotBeNull().Single().Message.ShouldContain("txncharges");
    }

    [Fact]
    public async Task The_importers_planned_trades_are_identical_with_and_without_the_cells()
    {
        var file = Ok(GlobalTransactionReader.Read(await SampleSheetAsync("global-transaction-report-shape.csv"), Today));
        var without = file with { Items = [.. file.Items.Select(i => i with { Lines = null })] };
        var index = new SecurityIndex([], [], [], []);

        var with = BillPlanner.Plan(file, index, []);
        var plain = BillPlanner.Plan(without, index, []);

        with.Identified.Count.ShouldBeGreaterThan(0);
        with.Fingerprint.ShouldBe(plain.Fingerprint);
        with.Identified.Select(i => (i.ExternalId, i.Trade)).ShouldBe(plain.Identified.Select(i => (i.ExternalId, i.Trade)));
        (with.Covered, with.NegativeChargeRows, with.InexactValues, with.ChargesTotal).ShouldBe((plain.Covered, plain.NegativeChargeRows, plain.InexactValues, plain.ChargesTotal));
    }
}
