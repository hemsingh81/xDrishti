using XDrishti.Application.Portfolio.Imports;
using XDrishti.Application.Portfolio.Reports;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportScenario;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// The oracle (P2-17 test plan): first-in first-out over the bill rows of a complete scrip gives the realised profit that Dhan's Realised P&amp;L statement reports for it. The statement is a made-up fixture in the shape of Dhan's; the profit is also worked out by hand here.
/// </summary>
public sealed class FifoOracleTests
{
    private static async Task<IReadOnlyDictionary<string, decimal>> RealisedByScripAsync()
    {
        var sheet = await SampleSheetAsync("realised-pnl-shape.csv");
        FileKindDetector.DetectSheet(sheet.Leading(FileKindDetector.LeadingRows).Select(r => r.Cells)).ShouldBe(FileKind.PnlReport);
        var header = sheet.Rows.First(r => r.Cells.Any(c => c.Contains("Scrip Name", StringComparison.Ordinal)));
        var map = new HeaderMap(header.Cells);
        var name = map.Find("scripname");
        var pnl = map.Find("realisedpl");
        return sheet.Rows.SkipWhile(r => r != header).Skip(1).Where(r => !r.IsBlank).ToDictionary(r => BillName.Parse(r.At(name)).Key, r => decimal.Parse(r.At(pnl), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task First_in_first_out_over_the_bill_rows_reproduces_the_statements_realised_profit_for_a_complete_scrip()
    {
        var oracle = await RealisedByScripAsync();
        var s = await ReportScenario.StartAsync(x =>
        {
            x.Hold("ZENITHE", Zenith, 30, 12m);
            x.ListSampleCompanies();
        });
        var text = Gtr(
        [
            GtrRow("07 Apr 2026 00:00:00", "ZENITH ENERGY SYSTEMS LTD", "NSE", "1001", 100, "1000.00", 0, "0.00", brokerage: "1.00"),
            GtrRow("20 Apr 2026 00:00:00", "ZENITH ENERGY SYSTEMS LTD", "NSE", "1002", 50, "600.00", 0, "0.00"),
            GtrRow("04 May 2026 00:00:00", "ZENITH ENERGY SYSTEMS LTD", "NSE", "1003", 0, "0.00", 120, "1800.00", brokerage: "2.00"),
        ]);

        var done = await s.ImportOkAsync(text);

        var expected = oracle[BillName.Parse("ZENITH ENERGY SYSTEMS LTD").Key];
        expected.ShouldBe(560m);
        done.Trades!.Report.Securities.Single().Rebuild.RealisedPnl.ShouldBe(expected);
    }

    [Fact]
    public async Task A_sale_across_three_lots_and_a_price_that_is_not_round_agree_with_the_hand_worked_profit_within_a_paisa_per_share()
    {
        var s = await ReportScenario.StartAsync(x =>
        {
            x.Hold("ORIONSTL", Orion, 5, 45m);
            x.ListSampleCompanies();
        });

        // 10 for 333.33 (33.333), 10 for 400.00 (40), 20 for 900.00 (45); then 35 sold for 1750.00 (50). FIFO: 10 + 10 + 15 of the third.
        // Cost: 333.33 + 400.00 + 15 × 45 = 1408.33; proceeds 1750.00; profit 341.67. The 5 shares at 45 that stay are the 5 the broker holds.
        var text = Gtr(
        [
            GtrRow("07 Apr 2026 00:00:00", "ORION STEEL LTD.", "NSE", "2001", 10, "333.33", 0, "0.00"),
            GtrRow("08 Apr 2026 00:00:00", "ORION STEEL LTD.", "NSE", "2002", 10, "400.00", 0, "0.00"),
            GtrRow("09 Apr 2026 00:00:00", "ORION STEEL LTD.", "NSE", "2003", 20, "900.00", 0, "0.00"),
            GtrRow("10 Apr 2026 00:00:00", "ORION STEEL LTD.", "NSE", "2004", 0, "0.00", 35, "1750.00"),
        ]);

        var preview = await s.PreviewOkAsync(text);

        var rebuild = preview.Trades!.Securities.Single().Rebuild;
        rebuild.RealisedPnl.ShouldNotBeNull();
        Math.Abs(rebuild.RealisedPnl!.Value - 341.67m).ShouldBeLessThanOrEqualTo(0.01m);
    }
}
