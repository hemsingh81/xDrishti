using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Text;

namespace XDrishti.SimExport.Tests;

/// <summary>
/// P3-03c, the hand-over of the export (spec P3-04b1 and P3-03c, D14): a costed run writes <c>costs</c> as <c>charged</c> and flattens <c>Costing</c> into the seventeen columns (<c>costing_*</c>, <c>cost_r</c>, <c>r_net</c>), the line columns
/// being the sum of the two legs; <c>run.json</c> holds the cost record of the run and the sixteen lines of every trade; the banner names the sheet, the sizing and the limits. A gross trade has seventeen empty cells.
/// </summary>
public sealed class CostedExportTests
{
    private static readonly string[] LineColumns = ["costing_brokerage", "costing_stt", "costing_exchange_txn", "costing_sebi_fee", "costing_stamp_duty", "costing_other", "costing_dp_charge", "costing_gst"];

    private static string Csv(SimulationRun run, IReadOnlyList<RunTrade> trades) => Sinks.Render(s => TradesCsv.Write(s, run.RunKey, trades));

    private static string Json(SimulationRun run) => Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-1"));

    private static decimal Cell(List<string> row, List<string> header, string column) => decimal.Parse(row[header.IndexOf(column)], NumberStyles.Float, CultureInfo.InvariantCulture);

    [Fact]
    public void The_costed_trades_file_equals_the_golden_file_golden()
    {
        var run = FabricatedRun.CostedStandard();

        Golden.Check("trades-csv-costed", Csv(run, run.Trades));
    }

    [Fact]
    public void A_costed_row_has_the_segment_the_sheet_the_quantity_the_turnovers_and_the_line_sums_of_the_two_legs()
    {
        var run = FabricatedRun.CostedStandard();
        var rows = Rfc4180Reader.Read(Csv(run, run.Trades));
        var header = rows[0];

        for (var i = 0; i < run.Trades.Count; i++)
        {
            var costing = run.Trades[i].Trade.Costing!;
            var row = rows[i + 1];
            row[header.IndexOf("costs")].ShouldBe("charged");
            row[header.IndexOf("costing_segment")].ShouldBe("intraday_equity");
            row[header.IndexOf("costing_sheet")].ShouldBe("test-synthetic-1");
            long.Parse(row[header.IndexOf("costing_quantity")], CultureInfo.InvariantCulture).ShouldBe(costing.Quantity);
            Cell(row, header, "costing_risk_taken").ShouldBe(costing.RiskTaken);
            Cell(row, header, "costing_entry_turnover").ShouldBe(costing.EntryTurnover);
            Cell(row, header, "costing_exit_turnover").ShouldBe(costing.ExitTurnover);
            Cell(row, header, "costing_brokerage").ShouldBe(costing.EntryLeg.Brokerage + costing.ExitLeg.Brokerage);
            Cell(row, header, "costing_stt").ShouldBe(costing.EntryLeg.Stt + costing.ExitLeg.Stt);
            Cell(row, header, "costing_exchange_txn").ShouldBe(costing.EntryLeg.ExchangeTxn + costing.ExitLeg.ExchangeTxn);
            Cell(row, header, "costing_sebi_fee").ShouldBe(costing.EntryLeg.SebiFee + costing.ExitLeg.SebiFee);
            Cell(row, header, "costing_stamp_duty").ShouldBe(costing.EntryLeg.StampDuty + costing.ExitLeg.StampDuty);
            Cell(row, header, "costing_other").ShouldBe(0m);
            Cell(row, header, "costing_dp_charge").ShouldBe(0m);
            Cell(row, header, "costing_gst").ShouldBe(costing.EntryLeg.Gst + costing.ExitLeg.Gst);
            Cell(row, header, "costing_total_cost").ShouldBe(costing.TotalCost);
            double.Parse(row[header.IndexOf("cost_r")], NumberStyles.Float, CultureInfo.InvariantCulture).ShouldBe(costing.CostR);
            double.Parse(row[header.IndexOf("r_net")], NumberStyles.Float, CultureInfo.InvariantCulture).ShouldBe(costing.RNet);
            var lines = LineColumns.Sum(c => Cell(row, header, c));
            lines.ShouldBe(Cell(row, header, "costing_total_cost"), "the eight groups add up to the cost");
        }
    }

    [Fact]
    public void A_gross_row_has_seventeen_empty_costing_cells_and_costs_none()
    {
        var run = FabricatedRun.Standard();
        var rows = Rfc4180Reader.Read(Csv(run, run.Trades));
        var header = rows[0];
        var costing = header.Where(c => c.StartsWith("costing_", StringComparison.Ordinal)).Concat(["cost_r", "r_net"]).ToList();

        costing.Count.ShouldBe(17);
        foreach (var row in rows.Skip(1))
        {
            costing.ShouldAllBe(c => row[header.IndexOf(c)] == string.Empty);
            row[header.IndexOf("costs")].ShouldBe("none");
        }
    }

    [Fact]
    public void The_csv_of_a_costed_run_is_the_same_under_another_culture_and_writes_no_negative_zero()
    {
        var run = FabricatedRun.CostedStandard();
        var expected = Csv(run, run.Trades);
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = comma;

            Csv(run, run.Trades).ShouldBe(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }

        expected.ShouldNotContain(",-0,");
    }

    [Fact]
    public void The_costed_json_equals_the_golden_file_golden()
    {
        Golden.Check("run-json-costed", Json(FabricatedRun.CostedStandard()));
    }

    [Fact]
    public void The_json_holds_the_cost_record_and_the_sixteen_lines_of_every_trade_with_canonical_decimals()
    {
        var run = FabricatedRun.CostedStandard();

        var root = JsonDocument.Parse(Json(run)).RootElement.GetProperty("run");

        var costs = root.GetProperty("costs");
        costs.GetProperty("sheet_id").GetString().ShouldBe("test-synthetic-1");
        costs.GetProperty("rates_sha256").GetString().ShouldBe(run.Costs!.RatesSha256);
        costs.GetProperty("sheet_json").GetString().ShouldBe(run.Costs.SheetJson);
        costs.GetProperty("checked").ValueKind.ShouldBe(JsonValueKind.Null);
        costs.GetProperty("capital").GetDecimal().ShouldBe(1_000_000m);
        costs.GetProperty("max_cost_r").GetDecimal().ShouldBe(0.10m);
        costs.GetProperty("profile").GetProperty("name").GetString().ShouldBe("standard");
        costs.GetProperty("profile").GetProperty("risk_per_trade_pct").GetDecimal().ShouldBe(0.5m);
        var trade = root.GetProperty("trades")[0].GetProperty("trade");
        var costing = trade.GetProperty("costing");
        var expected = run.Trades[0].Trade.Costing!;
        trade.GetProperty("costs").GetString().ShouldBe("charged");
        trade.GetProperty("schema_version").GetInt32().ShouldBe(5);
        costing.GetProperty("segment").GetString().ShouldBe("intraday_equity");
        costing.GetProperty("quantity").GetInt64().ShouldBe(expected.Quantity);
        costing.GetProperty("total_cost").GetDecimal().ShouldBe(expected.TotalCost);
        costing.GetProperty("cost_r").GetDouble().ShouldBe(expected.CostR);
        costing.GetProperty("r_net").GetDouble().ShouldBe(expected.RNet);
        foreach (var (name, leg) in new[] { ("entry_leg", expected.EntryLeg), ("exit_leg", expected.ExitLeg) })
        {
            var json = costing.GetProperty(name);
            json.EnumerateObject().Select(p => p.Name).ShouldBe(["brokerage", "stt", "exchange_txn", "sebi_fee", "stamp_duty", "other_charges", "dp_charge", "gst", "total"], name);
            json.GetProperty("brokerage").GetDecimal().ShouldBe(leg.Brokerage);
            json.GetProperty("gst").GetDecimal().ShouldBe(leg.Gst);
            json.GetProperty("total").GetDecimal().ShouldBe(leg.Total);
        }

        root.GetProperty("trades").EnumerateArray().Select(t => t.GetProperty("trade").GetProperty("costing").ValueKind).ShouldAllBe(k => k == JsonValueKind.Object);
        JsonDocument.Parse(Json(run)).RootElement.GetProperty("run").GetProperty("totals").GetProperty("fates").EnumerateObject().Select(p => p.Name).ShouldContain("skipped_quantity");
    }

    [Fact]
    public void A_gross_run_writes_costs_null_and_costing_null_in_json()
    {
        var run = FabricatedRun.Standard();

        var root = JsonDocument.Parse(Json(run)).RootElement.GetProperty("run");

        root.GetProperty("costs").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("trades")[0].GetProperty("trade").GetProperty("costing").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("trades")[0].GetProperty("trade").GetProperty("costs").GetString().ShouldBe("none");
    }

    [Fact]
    public void The_banner_of_a_costed_run_names_the_sheet_the_sizing_and_lists_the_limits_and_the_cases_equal_the_golden_file_golden()
    {
        var run = FabricatedRun.CostedStandard();
        var labels = RunLabels.Of(run, FillRules.Default);

        var text = RunBanner.Text(labels);

        text.ShouldContain("costs: sheet test-synthetic-1 (TEST sheet: synthetic rates, not the broker's) (not checked against bills)\n");
        text.ShouldContain("sizing: fixed simulated capital 1000000, profile standard");
        CostLimits.NotModelled.ShouldAllBe(l => text.Contains("not modelled: " + l.Code + ": ", StringComparison.Ordinal));
        FillLimits.NotModelled.ShouldAllBe(l => text.Contains("not modelled: " + l.Code + ": ", StringComparison.Ordinal));
        Golden.Check("banner-costed", text);
    }

    [Fact]
    public void Every_enum_of_the_costed_record_graph_still_has_a_spelling()
    {
        RecordGraph.Enums(typeof(SimulationRun)).Except(ExportSpellings.Enums).ShouldBeEmpty();
        RecordGraph.Enums(typeof(SimulationRun)).ShouldContain(typeof(CostSegment), "the graph reaches the costing");
        RecordGraph.DictionaryProperties(typeof(SimulationRun)).ShouldBe(["StrategyRecord.Parameters"], "no other dictionary was added by the costs");
    }
}
