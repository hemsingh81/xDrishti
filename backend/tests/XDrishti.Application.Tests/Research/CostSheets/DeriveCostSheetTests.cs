using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Research.CostSheets;
using XDrishti.Application.Tests.Portfolio.Reports;
using XDrishti.Domain.Research;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Research.CostSheets;

/// <summary>
/// CS-01 on invented report files with hand-worked numbers. The rates the files imply: delivery buy of 10,000.00 with STT 10.00 (0.1 %), exchange 0.30 (0.003 %), SEBI 0.10 (100 per crore), stamp 1.50 (0.015 %), other 0.10 (0.001 %) and GST 0.07 =
/// 17.5 % of (0.30 + 0.10); an intraday round trip of 10,000.00 bought and 10,000.00 sold with brokerage 4.80 (0.024 % of 20,000.00), STT 2.50 on the sell value (0.025 %), exchange 0.60, SEBI 0.20, stamp 0.30 on the buy value (0.003 %), other 0.20
/// and GST 0.98 = 17.5 % of (4.80 + 0.20 + 0.60). Priced back with those rates every bill agrees to the paisa.
/// </summary>
public sealed class DeriveCostSheetTests
{
    private static readonly string[] Sentinels = ["ZZ-SENTINEL-NAME", "ZZ-SENTINEL-MOBILE", "ZZ-SENTINEL-EMAIL", "ZZBILL", "ZZ SCRIP", ClientCode];

    private static string Day(int day) => day.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + " Apr 2026 00:00:00";

    private static string Delivery(int day, string stt = "10.00", string other = "0.10") =>
        GtrRow(Day(day), "ZZ SCRIP ONE", "NSE", "ZZBILL-D" + day, 100, "10000.00", 0, "0.00", gst: "0.07", stt: stt, sebi: "0.10", stamp: "1.50", txn: "0.30", other: other);

    private static string Intraday(int day) =>
        GtrRow(Day(day), "ZZ SCRIP TWO", "NSE", "ZZBILL-I" + day, 100, "10000.00", 100, "10000.00", brokerage: "4.80", gst: "0.98", stt: "2.50", sebi: "0.20", stamp: "0.30", txn: "0.60", other: "0.20");

    private static async Task<Domain.Common.Result<CostDerivation>> DeriveAsync(IEnumerable<string> rows, decimal split = 0.7m)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 5, 0, 0, TimeSpan.Zero));
        var handler = new DeriveCostSheetHandler(new FakeSpreadsheets(), time);
        return await handler.HandleAsync(new DeriveCostSheetQuery([Content(Gtr(rows))], split), TestContext.Current.CancellationToken);
    }

    private static async Task<CostDerivation> OkAsync(IEnumerable<string> rows, decimal split = 0.7m) => Ok(await DeriveAsync(rows, split));

    private static IEnumerable<string> BothClasses() => Enumerable.Range(1, 10).SelectMany(d => new[] { Delivery(d), Intraday(d) });

    [Fact]
    public async Task Both_classes_recover_the_hand_rates_and_every_bill_agrees_on_both_sets()
    {
        var derived = await OkAsync(BothClasses());

        (derived.DeriveSet.Bills, derived.DeriveSet.Within, derived.CheckSet.Bills, derived.CheckSet.Within).ShouldBe((14, 14, 6, 6));
        derived.DeriveSet.Passes.ShouldBeTrue();
        derived.CheckSet.Passes.ShouldBeTrue();
        derived.Largest.ShouldBeEmpty("every residual is 0.00");
        derived.SheetId.ShouldBe("derived-2026-10-15");
        (derived.From, derived.To).ShouldBe((new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 7)));
        derived.Lines.Single(l => l.Class == "all" && l.Line == "gst").Rate.ShouldBe(17.5m);
        derived.Lines.Single(l => l.Class == CostBillCheck.IntradayName && l.Line == "brokerage").Rate.ShouldBe(0.024m);
        derived.Lines.Single(l => l.Class == CostBillCheck.DeliveryName && l.Line == "stt").Rate.ShouldBe(0.1m);
        derived.Lines.Single(l => l.Class == CostBillCheck.DeliveryName && l.Line == "sebi").Rate.ShouldBe(100m);
        derived.Lines.ShouldAllBe(l => !l.Unstable);
    }

    [Fact]
    public async Task The_text_round_trips_through_the_parser_and_holds_the_hand_rates_and_no_checked_note()
    {
        var derived = await OkAsync(BothClasses());

        var sheet = CostSheetJson.Parse(derived.Json);

        sheet.IsSuccess.ShouldBeTrue(sheet.IsFailure ? sheet.Error.Code + ": " + string.Join("; ", sheet.Error.Fields?.Select(f => f.Field + " " + f.Message) ?? []) : string.Empty);
        sheet.Value.Id.ShouldBe("derived-2026-10-15");
        sheet.Value.Source.ShouldBe("derived from 14 bills, 2026-04-01..2026-04-07");
        sheet.Value.Checked.ShouldBeNull();
        var schedule = sheet.Value.Schedules.ShouldHaveSingleItem();
        schedule.EffectiveFrom.ShouldBe(new DateOnly(2026, 4, 1));
        schedule.GstPct.ShouldBe(17.5m);
        var intraday = schedule.IntradayEquity;
        (intraday.BrokeragePct, intraday.BrokerageCap, intraday.BrokerageMin, intraday.SttBuyPct, intraday.SttSellPct).ShouldBe((0.024m, 4.8m, 0m, 0m, 0.025m));
        (intraday.ExchangeTxnPct, intraday.SebiPerCrore, intraday.StampBuyPct, intraday.OtherPct).ShouldBe((0.003m, 100m, 0.003m, 0.001m));
        var delivery = schedule.Delivery;
        (delivery.BrokeragePct, delivery.BrokerageCap, delivery.SttBuyPct, delivery.SttSellPct, delivery.StampBuyPct, delivery.OtherPct).ShouldBe((0m, 0m, 0.1m, 0.1m, 0.015m, 0.001m));
        derived.Json.ShouldNotContain("\"checked\"");
    }

    [Fact]
    public async Task A_line_without_bills_is_null_in_the_text_unavailable_in_the_lines_and_the_text_cannot_be_loaded()
    {
        var derived = await OkAsync(Enumerable.Range(1, 10).Select(d => Delivery(d, other: "0.00")));

        derived.Lines.Single(l => l.Class == CostBillCheck.DeliveryName && l.Line == "other").Rate.ShouldBeNull();
        derived.Lines.ShouldNotContain(l => l.Class == CostBillCheck.IntradayName, "a class without bills has no lines");
        derived.Json.ShouldContain("\"other_pct\": null");
        derived.Json.ShouldContain("\"brokerage_pct\": null", Case.Sensitive, "the class without bills is null throughout");
        CostSheetJson.Parse(derived.Json).IsFailure.ShouldBeTrue();
        (derived.DeriveSet.Within, derived.DeriveSet.Bills).ShouldBe((7, 7), "a null line prices as the 0 the bills show");
    }

    [Fact]
    public async Task The_split_is_by_date_the_newest_bills_are_not_used_for_the_rates_and_a_change_there_fails_the_check_set()
    {
        // the last three bills (days 8 to 10) carry STT 20.00: the derive set (days 1 to 7) must not see it
        var rows = Enumerable.Range(1, 10).Select(d => Delivery(d, stt: d > 7 ? "20.00" : "10.00"));

        var derived = await OkAsync(rows);

        derived.Lines.Single(l => l.Line == "stt").Rate.ShouldBe(0.1m);
        (derived.DeriveSet.Bills, derived.DeriveSet.Within).ShouldBe((7, 7));
        (derived.CheckSet.Bills, derived.CheckSet.Within).ShouldBe((3, 0));
        derived.CheckSet.Passes.ShouldBeFalse();
        derived.Largest.Select(r => (r.Date, r.Set, r.Billed, r.Modelled, r.Difference)).ShouldBe(
        [
            (new DateOnly(2026, 4, 8), "check", 22.07m, 12.07m, -10m),
            (new DateOnly(2026, 4, 9), "check", 22.07m, 12.07m, -10m),
            (new DateOnly(2026, 4, 10), "check", 22.07m, 12.07m, -10m),
        ]);
        derived.ResidualsCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(11, "a header and all ten bills");
        derived.ResidualsCsv.ShouldContain("2026-04-10,delivery equity,check,22.07,12.07,-10.00");
    }

    [Fact]
    public async Task A_split_never_cuts_a_day_in_two()
    {
        // 14 bills on 7 days: 0.5 would cut after bill 7, in the middle of day 4, so day 4 stays whole
        var derived = await OkAsync(BothClasses().Take(14), split: 0.5m);

        (derived.DeriveSet.Bills, derived.CheckSet.Bills).ShouldBe((8, 6));
        derived.To.ShouldBe(new DateOnly(2026, 4, 4));
    }

    [Fact]
    public async Task A_rate_that_moves_more_than_5_percent_is_unstable_with_its_date_range()
    {
        var rows = Enumerable.Range(1, 10).Select(d => Delivery(d, stt: d is 2 or 3 ? "12.00" : "10.00"));

        var derived = await OkAsync(rows);

        var stt = derived.Lines.Single(l => l.Line == "stt");
        stt.Unstable.ShouldBeTrue();
        (stt.From, stt.To).ShouldBe((new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 7)));
        stt.Rate.ShouldBe(0.1m, "the median, not the mean");
        derived.Lines.Where(l => l.Line != "stt").ShouldAllBe(l => !l.Unstable);
        derived.DeriveSet.Passes.ShouldBeFalse("two of the seven bills are 2.00 off");
    }

    [Fact]
    public async Task Rows_that_fit_no_class_are_not_used_and_derivatives_are_skipped()
    {
        var rows = new[]
        {
            Delivery(1),
            Delivery(2),
            GtrRow(Day(3), "ZZ SCRIP ODD", "NSE", "ZZBILL-X", 100, "10000.00", 0, "0.00", brokerage: "5.00"),
            GtrRow(Day(4), "ZZ SCRIP ONE", "NSE", "ZZBILL-N", 100, "10000.00", 0, "0.00", other: "-0.01"),
            GtrRow(Day(5), "FUT ZZ SCRIP FOUR 28APR2026", "NFO", "ZZBILL-F", 50, "5000.00", 0, "0.00", brokerage: "20.00"),
        };

        var derived = await OkAsync(rows);

        derived.Unclassified.ShouldBe(2, "a one-sided row with brokerage and an adjustment line");
        derived.Skipped.Select(s => (s.Reason, s.Rows)).ShouldBe([("derivatives", 1)]);
        (derived.DeriveSet.Bills + derived.CheckSet.Bills).ShouldBe(2);
    }

    [Fact]
    public async Task Nothing_the_report_holds_about_a_person_or_a_bill_is_in_the_answer()
    {
        var derived = await OkAsync(BothClasses());

        var everything = derived.Json + derived.ResidualsCsv + string.Join('\n', derived.Lines.Select(l => l.ToString())) + string.Join('\n', derived.Largest.Select(l => l.ToString()));
        foreach (var sentinel in Sentinels)
        {
            everything.ShouldNotContain(sentinel);
        }
    }

    [Fact]
    public async Task Without_a_usable_row_a_file_that_is_not_a_report_or_a_split_out_of_range_the_derivation_is_refused()
    {
        var empty = await DeriveAsync([GtrRow(Day(1), "FUT ZZ SCRIP FOUR 28APR2026", "NFO", "ZZBILL-F", 50, "5000.00", 0, "0.00", brokerage: "20.00")]);
        var tooLow = await DeriveAsync([Delivery(1)], split: 0.4m);
        var tooHigh = await DeriveAsync([Delivery(1)], split: 0.95m);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 5, 0, 0, TimeSpan.Zero));
        var handler = new DeriveCostSheetHandler(new FakeSpreadsheets(), time);
        var noFiles = await handler.HandleAsync(new DeriveCostSheetQuery([], 0.7m), TestContext.Current.CancellationToken);
        var notAReport = await handler.HandleAsync(new DeriveCostSheetQuery([Content("a,b\n1,2\n")], 0.7m), TestContext.Current.CancellationToken);

        empty.IsFailure.ShouldBeTrue();
        tooLow.IsFailure.ShouldBeTrue();
        tooHigh.IsFailure.ShouldBeTrue();
        noFiles.IsFailure.ShouldBeTrue();
        notAReport.IsFailure.ShouldBeTrue();
        notAReport.Error.Message.ShouldStartWith("File 1: ");
    }
}
