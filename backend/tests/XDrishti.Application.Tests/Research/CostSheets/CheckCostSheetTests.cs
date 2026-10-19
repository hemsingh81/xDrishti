using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Research.CostSheets;
using XDrishti.Application.Tests.Portfolio.Reports;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Research.CostSheets;

/// <summary>
/// Criterion 10 of P3-03f on invented report files and the invented sheet <c>fixture-a</c> (the numbers of the TEST sheet; schedule B from 2026-03-03 is in force on every row). Every cell is a hand figure of the schedule: a delivery buy of
/// 10,000.00 is STT 8.00, exchange 0.40, SEBI 0.05, stamp 3.00 and GST 18 % of 0.45 = 0.081 = 0.08; a delivery sell of 20,000.00 is STT 18.00, exchange 0.80, SEBI 0.10, GST 0.162 = 0.16; the round trip of 10,000.00 bought and 10,500.00 sold is
/// brokerage 3.00 + 3.15, STT 3.675 = 3.68, exchange 0.50 + 0.525 = 0.53 (1.03), SEBI 0.05 + 0.05, stamp 2.00, GST 0.639 = 0.64 and 0.6714 = 0.67 (1.31).
/// </summary>
public sealed class CheckCostSheetTests
{
    private const string D1 = "07 Apr 2026 00:00:00";

    private const string D2 = "08 Apr 2026 00:00:00";

    private const string D3 = "09 Apr 2026 00:00:00";

    private const string D4 = "10 Apr 2026 00:00:00";

    private const string D5 = "13 Apr 2026 00:00:00";

    private const string D6 = "14 Apr 2026 00:00:00";

    private const string D7 = "15 Apr 2026 00:00:00";

    private const string D8 = "16 Apr 2026 00:00:00";

    private static readonly string[] Sentinels = ["ZZ-SENTINEL-NAME", "ZZ-SENTINEL-MOBILE", "ZZ-SENTINEL-EMAIL", "ZZBILL", "ZZ SCRIP", ClientCode, "10000.00", "20000.00"];

    private static string DeliveryBuy(string date, string bill, string txn = "0.40", string other = "0.00") =>
        GtrRow(date, "ZZ SCRIP ONE", "NSE", bill, 100, "10000.00", 0, "0.00", gst: "0.08", stt: "8.00", sebi: "0.05", stamp: "3.00", txn: txn, other: other);

    private static readonly string[] Rows =
    [
        DeliveryBuy(D1, "ZZBILL01"),
        DeliveryBuy(D2, "ZZBILL02", txn: "0.44"),
        DeliveryBuy(D3, "ZZBILL03", txn: "0.46"),
        GtrRow(D4, "ZZ SCRIP TWO", "NSE", "ZZBILL04", 0, "0.00", 200, "20000.00", gst: "0.16", stt: "18.00", sebi: "0.10", txn: "0.80"),
        GtrRow(D5, "ZZ SCRIP THREE", "NSE", "ZZBILL05", 100, "10000.00", 100, "10500.00", brokerage: "6.15", gst: "1.31", stt: "3.68", sebi: "0.10", stamp: "2.00", txn: "1.03"),
        DeliveryBuy(D6, "ZZBILL06", other: "-0.01"),
        GtrRow(D7, "FUT ZZ SCRIP FOUR 28APR2026", "NFO", "ZZBILL07", 50, "5000.00", 0, "0.00", brokerage: "20.00"),
        GtrRow(D8, "ZZ SCRIP FIVE", "NSE", "ZZBILL08", 100, "10000.00", 0, "0.00", brokerage: "5.00", gst: "0.90", stt: "8.00", sebi: "0.05", stamp: "3.00", txn: "0.40"),
    ];

    private static async Task<CostCheckReport> CheckAsync(CostSheet? sheet = null, params string[] files)
    {
        var store = new FakeCostSheetStore().With(sheet ?? FixtureSheet.Sheet);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 5, 0, 0, TimeSpan.Zero));
        var handler = new CheckCostSheetHandler(store, new FakeSpreadsheets(), time);
        var contents = (files.Length == 0 ? [Gtr(Rows)] : files).Select(Content).ToList();

        var result = await handler.HandleAsync(new CheckCostSheetQuery((sheet ?? FixtureSheet.Sheet).Id, contents), TestContext.Current.CancellationToken);

        return Ok(result);
    }

    [Fact]
    public async Task The_classes_and_counts_are_right_and_exactly_one_row_is_beyond_the_tolerance()
    {
        var report = await CheckAsync();

        report.RowsRead.ShouldBe(8);
        var delivery = report.Classes.Single(c => c.Name == CostBillCheck.DeliveryName);
        var intraday = report.Classes.Single(c => c.Name == CostBillCheck.IntradayName);
        (delivery.Rows, delivery.Passed, delivery.Beyond).ShouldBe((4, 3, 1));
        (intraday.Rows, intraday.Passed, intraday.Beyond).ShouldBe((1, 1, 0));
        delivery.LargestDifference.ShouldBe(0.06m);
        intraday.LargestDifference.ShouldBe(0m);
        report.Classes.Sum(c => c.Beyond).ShouldBe(1);
    }

    [Fact]
    public async Task A_row_off_by_0_04_passes_and_one_off_by_0_06_does_not()
    {
        var report = await CheckAsync();

        report.Largest.Select(r => (r.Date, r.Class, r.Difference)).ShouldBe(
        [
            (new DateOnly(2026, 4, 9), CostBillCheck.DeliveryName, -0.06m),
            (new DateOnly(2026, 4, 8), CostBillCheck.DeliveryName, -0.04m),
        ]);
    }

    [Fact]
    public async Task The_negative_other_cell_is_an_adjustment_and_the_row_is_not_counted()
    {
        var report = await CheckAsync();

        (report.AdjustmentRows, report.AdjustmentSum).ShouldBe((1, -0.01m));
        report.Classes.Sum(c => c.Rows).ShouldBe(5, "eight rows less the adjustment, the futures row and the row that fits no class");
    }

    [Fact]
    public async Task The_futures_row_is_skipped_with_its_reason_and_the_odd_row_is_listed_and_not_compared()
    {
        var report = await CheckAsync();

        report.Skipped.Select(s => (s.Reason, s.Rows)).ShouldBe([("derivatives", 1)]);
        report.UnclassifiedTotal.ShouldBe(1);
        var odd = report.Unclassified.ShouldHaveSingleItem();
        odd.Date.ShouldBe(new DateOnly(2026, 4, 16));
        odd.Reason.ShouldContain("brokerage");
    }

    [Fact]
    public async Task The_largest_difference_of_each_line_is_the_hand_figure()
    {
        var report = await CheckAsync();

        var delivery = report.Classes.Single(c => c.Name == CostBillCheck.DeliveryName);
        delivery.Lines.Select(l => (l.Name, l.LargestDifference)).ShouldBe(
        [
            ("brokerage", 0m), ("gst", 0m), ("stt", 0m), ("sebi", 0m), ("stamp duty", 0m), ("txn charges", 0.06m), ("other", 0m),
        ]);
        report.Classes.Single(c => c.Name == CostBillCheck.IntradayName).Lines.ShouldAllBe(l => l.LargestDifference == 0m);
    }

    [Fact]
    public async Task The_implied_rates_equal_a_hand_calculation()
    {
        var report = await CheckAsync();

        var lines = report.Classes.Single(c => c.Name == CostBillCheck.DeliveryName).Lines.ToDictionary(l => l.Name);
        lines["brokerage"].Implied.ShouldBeNull("no delivery row has a brokerage cell above 0");
        lines["txn charges"].Implied.ShouldBe(new CostCheckImplied(0.004m, 0.0042m, 0.0046m, 4), "0.40/10,000, 0.44/10,000, 0.46/10,000 and 0.80/20,000, in percent; the median of the two middle values 0.004 and 0.0044");
        lines["stt"].Implied.ShouldBe(new CostCheckImplied(0.08m, 0.08m, 0.09m, 4), "8.00/10,000 three times and 18.00/20,000");
        lines["sebi"].Implied.ShouldBe(new CostCheckImplied(50m, 50m, 50m, 4), "rupees per crore: 0.05 and 0.10");
        lines["stamp duty"].Implied.ShouldBe(new CostCheckImplied(0.03m, 0.03m, 0.03m, 3), "the sell row has no stamp duty");
        lines["gst"].Implied.ShouldBe(new CostCheckImplied(15.686275m, 17.052155m, 17.777778m, 4), "0.08/0.51, 0.08/0.49, 0.08/0.45 and 0.16/0.90, in percent of the brokerage, sebi and transaction cells");
        lines["other"].Implied.ShouldBeNull();
        var intraday = report.Classes.Single(c => c.Name == CostBillCheck.IntradayName).Lines.ToDictionary(l => l.Name);
        intraday["brokerage"].Implied.ShouldBe(new CostCheckImplied(0.03m, 0.03m, 0.03m, 1), "6.15 over 20,500");
        intraday["stt"].Implied.ShouldBe(new CostCheckImplied(0.035048m, 0.035048m, 0.035048m, 1), "3.68 over the sell value 10,500, in percent, to six places");
    }

    [Fact]
    public async Task No_suggested_note_is_made_while_a_row_is_beyond_the_tolerance()
    {
        (await CheckAsync()).SuggestedChecked.ShouldBeNull();
    }

    [Fact]
    public async Task When_every_row_is_within_the_tolerance_a_note_is_suggested_and_it_names_the_day_and_the_counts()
    {
        var clean = Gtr([Rows[0], Rows[1], Rows[3], Rows[4]]);

        var report = await CheckAsync(files: clean);

        report.SuggestedChecked.ShouldBe("check 2026-10-15: 1 intraday rows within 0.05, 3 delivery rows within 0.05");
    }

    [Fact]
    public async Task Nothing_that_identifies_a_bill_reaches_the_report()
    {
        var report = await CheckAsync();

        var text = JsonSerializer.Serialize(report);
        foreach (var sentinel in Sentinels)
        {
            text.ShouldNotContain(sentinel);
        }
    }

    [Fact]
    public async Task Two_files_that_repeat_a_bill_count_it_once()
    {
        var one = Gtr([Rows[0], Rows[1]]);
        var two = Gtr([Rows[1], Rows[3]]);

        var report = await CheckAsync(files: [one, two]);

        report.Files.ShouldBe(2);
        report.RowsRead.ShouldBe(3);
        report.RowsRepeated.ShouldBe(1);
    }

    [Fact]
    public async Task A_sheet_with_two_schedules_is_compared_by_the_schedule_of_each_rows_date()
    {
        // 2026-03-02 is schedule A (intraday sell STT 0.04 %), 2026-03-03 is schedule B (0.035 %): the same bill is 4.20 of STT by A and 3.68 by B; the bill shows 3.68
        var onA = GtrRow("02 Mar 2026 00:00:00", "ZZ SCRIP THREE", "NSE", "ZZBILL05", 100, "10000.00", 100, "10500.00", brokerage: "6.15", gst: "1.31", stt: "3.68", sebi: "0.10", stamp: "2.00", txn: "1.03");
        var onB = GtrRow("03 Mar 2026 00:00:00", "ZZ SCRIP THREE", "NSE", "ZZBILL06", 100, "10000.00", 100, "10500.00", brokerage: "6.15", gst: "1.31", stt: "3.68", sebi: "0.10", stamp: "2.00", txn: "1.03");

        var report = await CheckAsync(files: Gtr([onA, onB]));

        var intraday = report.Classes.Single();
        (intraday.Rows, intraday.Passed, intraday.Beyond).ShouldBe((2, 1, 1));
        report.Largest.Single().Date.ShouldBe(new DateOnly(2026, 3, 2));
        report.Largest.Single().Difference.ShouldBe(0.52m, "10,500 x 0.04 % = 4.20 against the billed 3.68");
    }

    [Fact]
    public async Task A_date_before_the_first_schedule_is_unclassified_and_not_compared()
    {
        var early = GtrRow("02 Jan 2025 00:00:00", "ZZ SCRIP ONE", "NSE", "ZZBILL01", 100, "10000.00", 0, "0.00", gst: "0.08", stt: "8.00", sebi: "0.05", stamp: "3.00", txn: "0.40");
        var store = new FakeCostSheetStore().With(TestCostSheet.Of("fixture-late", TestCostSheet.Schedule(new DateOnly(2026, 1, 1), TestCostSheet.IntradayA, TestCostSheet.DeliveryA)));
        var handler = new CheckCostSheetHandler(store, new FakeSpreadsheets(), new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 5, 0, 0, TimeSpan.Zero)));

        var result = await handler.HandleAsync(new CheckCostSheetQuery("fixture-late", [Content(Gtr([early]))]), TestContext.Current.CancellationToken);

        var report = Ok(result);
        report.Classes.ShouldBeEmpty();
        report.Unclassified.ShouldHaveSingleItem().Reason.ShouldContain("no rates");
    }

    [Fact]
    public async Task An_unknown_sheet_and_a_file_the_reader_refuses_are_failures_naming_nothing_of_the_file()
    {
        var handler = new CheckCostSheetHandler(new FakeCostSheetStore().With(FixtureSheet.Sheet), new FakeSpreadsheets(), new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 5, 0, 0, TimeSpan.Zero)));

        var unknown = await handler.HandleAsync(new CheckCostSheetQuery("nope", [Content(Gtr(Rows))]), TestContext.Current.CancellationToken);
        var bad = await handler.HandleAsync(new CheckCostSheetQuery(FixtureSheet.Id, [Content("not,a,report\n1,2,3\n")]), TestContext.Current.CancellationToken);
        var none = await handler.HandleAsync(new CheckCostSheetQuery(FixtureSheet.Id, []), TestContext.Current.CancellationToken);

        unknown.Error.Code.ShouldBe(CostErrors.SheetNotFoundCode);
        bad.IsFailure.ShouldBeTrue();
        bad.Error.Fields.ShouldNotBeNull().Single().Message.ShouldStartWith("File 1:");
        none.IsFailure.ShouldBeTrue();
    }
}
