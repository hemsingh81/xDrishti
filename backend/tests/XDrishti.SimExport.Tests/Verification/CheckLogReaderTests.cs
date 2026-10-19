using System.Text;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Verification;

namespace XDrishti.SimExport.Tests.Verification;

/// <summary>
/// Criterion 10 of P3-09 (Part B), the library side: the comparison of a filled log with the engine's values, field by field, on a pack of invented trades. Every case of the criterion has a row; the output carries no R value, no statistic and never
/// says that P3 passes; the owner's text is echoed without Control and Format characters.
/// </summary>
public sealed class CheckLogReaderTests
{
    private static readonly DateOnly Day = new(2025, 4, 8);

    private static PackPlan Plan(int trades, int spots = 0)
    {
        var rows = Enumerable.Range(0, trades).Select(i => new PackRow(i + 1, 0, new RunTrade(PackFixtures.Neutral("ORB", "NSE:S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(i)), []), PackPlanner.RandomSource, [], 0.05m)).ToList();
        var lines = Enumerable.Range(0, spots).Select(i => new SpotRow(trades + i + 1, SpotChecks.FlaggedDay, string.Empty, "NSE:S00", Day.AddDays(300 + i), 0)).ToList();
        return new PackPlan(rows, lines, EdgeCategories.Codes.ToDictionary(c => c, _ => false));
    }

    private static string EngineText(PackPlan plan) => Sinks.Render(s => PackFiles.WriteEngineValues(s, plan));

    /// <summary>The log of a careful owner: every trade row equal to the engine's values, signed agree on the card, the spot rows agreeing.</summary>
    private static List<Dictionary<string, string>> Perfect(PackPlan plan)
    {
        var engine = Rfc4180Reader.Read(EngineText(plan));
        var header = engine[0];
        var rows = new List<Dictionary<string, string>>();
        foreach (var cells in engine.Skip(1))
        {
            var e = header.Zip(cells).ToDictionary(p => p.First, p => p.Second);
            var row = PackFiles.CheckLogColumns.ToDictionary(c => c, _ => string.Empty);
            row["seq"] = e["seq"];
            row["trade_key"] = e["trade_key"];
            if (e["source"] == PackPlanner.SpotSource)
            {
                row["kind"] = "spot";
                row["verdict"] = "agree";
            }
            else
            {
                row["kind"] = "trade";
                row["chart"] = "card";
                row["seen_entry_time"] = e["entry_ist"][11..];
                row["seen_entry_price"] = e["entry_price"];
                row["seen_stop"] = e["stop_price"];
                row["seen_target"] = e["target_price"];
                row["seen_exit_time"] = e["exit_ist"];
                row["seen_exit_price"] = e["exit_price"];
                row["seen_exit_reason"] = e["exit_reason"];
                row["seen_r"] = e["r_exact"];
                row["verdict"] = "agree";
                row["signed_on"] = "2026-10-20";
            }

            rows.Add(row);
        }

        return rows;
    }

    private static void External(Dictionary<string, string> row, bool columns = true)
    {
        row["chart"] = "external:TradingView";
        if (columns)
        {
            (row["ext_open"], row["ext_high"], row["ext_low"], row["ext_close"]) = ("100.5", "101", "100", "100.5");
        }
    }

    private static string Csv(IEnumerable<Dictionary<string, string>> rows, string lineEnd = "\n", bool bom = false)
    {
        string Cell(string value) => value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
        var text = new StringBuilder(bom ? "﻿" : string.Empty);
        text.Append(string.Join(',', PackFiles.CheckLogColumns)).Append(lineEnd);
        foreach (var row in rows)
        {
            text.Append(string.Join(',', PackFiles.CheckLogColumns.Select(c => Cell(row[c])))).Append(lineEnd);
        }

        return text.ToString();
    }

    private static string Line(CheckReport report, int seq) => report.Lines.First(l => l.StartsWith("row " + seq.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ", StringComparison.Ordinal));

    private static string Add(string number, string delta) => (decimal.Parse(number, System.Globalization.CultureInfo.InvariantCulture) + decimal.Parse(delta, System.Globalization.CultureInfo.InvariantCulture)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Every_case_of_the_criterion_gets_the_line_of_the_protocol_and_the_pack_is_incomplete_with_the_reasons()
    {
        var plan = Plan(10);
        var rows = Perfect(plan);
        rows[1]["seen_entry_price"] = Add(rows[1]["seen_entry_price"], "0.05");
        (rows[2]["seen_stop"], rows[2]["verdict"], rows[2]["finding"]) = (Add(rows[2]["seen_stop"], "0.10"), "disagree", "data: stop level read off the wrong bar");
        (rows[3]["seen_r"], rows[3]["verdict"], rows[3]["finding"]) = (Add(rows[3]["seen_r"], "0.011"), "disagree", "data: own arithmetic");
        (rows[4]["seen_exit_reason"], rows[4]["verdict"], rows[4]["finding"]) = ("target", "disagree", "data: the target was a wick");
        rows[5]["seen_exit_price"] = Add(rows[5]["seen_exit_price"], "0.5");
        (rows[6]["seen_exit_price"], rows[6]["verdict"], rows[6]["finding"]) = (Add(rows[6]["seen_exit_price"], "0.5"), "disagree", "data: ISS-21 widened 09:15 bar");
        (rows[7]["verdict"], rows[7]["notes"]) = ("not_decidable", "the order inside the minute is not in the data");
        rows[8]["verdict"] = string.Empty;
        External(rows[9], columns: false);

        var report = CheckLogReader.Read(Csv(rows), EngineText(plan));

        report.Problems.ShouldBeEmpty();
        Line(report, 1).ShouldEndWith(": ok; agree");
        Line(report, 2).ShouldContain(": ok within tolerance; agree");
        Line(report, 3).ShouldContain(": beyond tolerance: seen_stop;");
        Line(report, 4).ShouldContain(": beyond tolerance: seen_r;");
        Line(report, 5).ShouldContain(": mismatch: seen_exit_reason;");
        Line(report, 6).ShouldContain("beyond tolerance: seen_exit_price; inconsistent: signed agree with a field beyond tolerance, unexplained until changed or given a finding");
        Line(report, 7).ShouldContain("disagree, explained by a finding (data: ISS-21 widened 09:15 bar)");
        Line(report, 8).ShouldContain(": explained (not decidable from 1-minute data; note: the order inside the minute is not in the data)");
        Line(report, 9).ShouldEndWith(": unsigned");
        Line(report, 10).ShouldContain(": missing: ext_open, ext_high, ext_low, ext_close;");
        report.Lines.Where(l => l.StartsWith("  seen_stop: ", StringComparison.Ordinal)).ShouldContain("  seen_stop: beyond tolerance");
        report.Lines.ShouldContain("  seen_exit_reason: mismatch");
        report.Unexplained.ShouldBe(1, "the inconsistent row; the disagree with a finding is explained");
        report.SignedRows.ShouldBe(8);
        report.External.ShouldBe(1);
        report.Complete.ShouldBeFalse();
        report.Lines[^1].ShouldStartWith("pack: incomplete (");
        report.Lines[^1].ShouldContain("1 rows unsigned");
        report.Lines[^1].ShouldContain("1 rows unexplained");
        report.Lines[^1].ShouldContain("fewer than 5 rows against an external chart");
        report.Lines[^1].ShouldContain("rows with a field missing or unreadable");
    }

    [Fact]
    public void The_lines_of_the_pack_come_after_every_row_and_say_the_counts_of_the_sheet()
    {
        var plan = Plan(6);

        var report = CheckLogReader.Read(Csv(Perfect(plan)), EngineText(plan));

        var firstPackLine = report.Lines.ToList().FindIndex(l => l.StartsWith("rows in the pack", StringComparison.Ordinal));
        report.Lines.Take(firstPackLine).Count(l => l.StartsWith("row ", StringComparison.Ordinal)).ShouldBe(6);
        report.Lines.Skip(firstPackLine).ShouldNotContain(l => l.StartsWith("row ", StringComparison.Ordinal));
        report.Lines.ShouldContain("rows signed: 6");
        report.Lines.ShouldContain("rows against an external chart: 0 (at least 5 are needed)");
        report.Lines.ShouldContain("rows unexplained: 0");
        report.Lines.ShouldContain("findings by class: data 0, model 0, implementation 0, unclassified 0");
    }

    [Fact]
    public void With_every_row_signed_and_explained_and_five_external_rows_the_pack_is_complete_and_the_output_never_says_that_p3_passes()
    {
        var plan = Plan(8, spots: 2);
        var rows = Perfect(plan);
        foreach (var row in rows.Take(5))
        {
            External(row);
        }

        (rows[5]["verdict"], rows[5]["seen_exit_price"], rows[5]["finding"]) = ("disagree", Add(rows[5]["seen_exit_price"], "1"), "data: ISS-20 unadjusted jump");
        (rows[9]["verdict"], rows[9]["notes"]) = ("skipped", "no chart reaches back that far");

        var report = CheckLogReader.Read(Csv(rows), EngineText(plan));

        report.Problems.ShouldBeEmpty();
        report.Complete.ShouldBeTrue(string.Join('\n', report.Lines));
        report.Lines[^1].ShouldBe("pack: complete");
        report.External.ShouldBe(5);
        report.Unexplained.ShouldBe(0);
        Line(report, 10).ShouldContain("skipped (reason: no chart reaches back that far)");
        var text = string.Join('\n', report.Lines);
        text.ShouldNotContain("pass", Case.Insensitive);
        text.ShouldNotContain("P3 ");
        ForbiddenWords.In(text).ShouldBeEmpty();
        System.Text.RegularExpressions.Regex.IsMatch(text, @"[-+]?\d+\.\d{2,}").ShouldBeFalse("no price and no R value is printed");
    }

    [Fact]
    public void A_disagree_without_a_finding_a_skipped_trade_row_a_skipped_spot_without_a_reason_and_an_unknown_verdict_leave_the_pack_incomplete()
    {
        var plan = Plan(3, spots: 1);
        var rows = Perfect(plan);
        (rows[0]["verdict"], rows[0]["seen_stop"]) = ("disagree", Add(rows[0]["seen_stop"], "1"));
        rows[1]["verdict"] = "skipped";
        rows[2]["verdict"] = "maybe";
        rows[3]["verdict"] = "skipped";

        var report = CheckLogReader.Read(Csv(rows), EngineText(plan));

        Line(report, 1).ShouldContain("disagree, unexplained: no finding");
        Line(report, 2).ShouldContain("a trade row cannot be skipped");
        Line(report, 3).ShouldContain("unsigned (the verdict 'maybe' is not one of");
        Line(report, 4).ShouldContain("skipped without a reason");
        report.Unexplained.ShouldBe(1);
        report.Complete.ShouldBeFalse();
        report.Lines[^1].ShouldContain("skipped without a reason");
    }

    [Fact]
    public void A_model_or_implementation_finding_not_marked_closed_keeps_the_pack_incomplete_and_the_classes_are_counted()
    {
        var plan = Plan(6);
        var rows = Perfect(plan);
        foreach (var row in rows.Take(5))
        {
            External(row);
        }

        (rows[0]["verdict"], rows[0]["seen_exit_price"], rows[0]["finding"]) = ("disagree", Add(rows[0]["seen_exit_price"], "1"), "model: the square-off at the open of 15:15");
        (rows[1]["verdict"], rows[1]["seen_exit_price"], rows[1]["finding"]) = ("disagree", Add(rows[1]["seen_exit_price"], "1"), "implementation: wrong tick closed in commit abc");
        (rows[2]["verdict"], rows[2]["seen_exit_price"], rows[2]["finding"]) = ("disagree", Add(rows[2]["seen_exit_price"], "1"), "something else");

        var report = CheckLogReader.Read(Csv(rows), EngineText(plan));

        report.Complete.ShouldBeFalse();
        report.Lines.ShouldContain("findings by class: data 0, model 1, implementation 1, unclassified 1");
        report.Lines[^1].ShouldContain("1 model or implementation findings not marked closed");
    }

    [Fact]
    public void The_optional_calculated_columns_are_compared_only_when_the_owner_adds_and_fills_them()
    {
        var plan = Plan(1);
        var engine = Rfc4180Reader.Read(EngineText(plan));
        var e = engine[0].Zip(engine[1]).ToDictionary(p => p.First, p => p.Second);
        var rows = Perfect(plan);
        var withColumns = Csv(rows).Replace("signed_on", "signed_on,calc_entry_filled,calc_exit_filled,calc_r_filled", StringComparison.Ordinal);
        var lines = withColumns.Split('\n');
        lines[1] += "," + e["entry_filled_price"] + "," + Add(e["exit_filled_price"], "1") + ",";
        var text = string.Join('\n', lines);

        var report = CheckLogReader.Read(text, EngineText(plan));

        report.Problems.ShouldBeEmpty();
        report.Lines.ShouldContain("  calc_entry_filled: ok");
        report.Lines.ShouldContain("  calc_exit_filled: beyond tolerance");
        report.Lines.ShouldNotContain(l => l.StartsWith("  calc_r_filled", StringComparison.Ordinal), "an empty optional cell is not compared");
    }

    [Fact]
    public void Windows_line_ends_a_byte_order_mark_and_quoted_cells_are_read_and_a_defused_key_still_matches()
    {
        var hostile = new PackRow(1, 0, new RunTrade(PackFixtures.Neutral("-ORB", "NSE:A,B&C", Day), []), PackPlanner.RandomSource, [], 0.05m);
        var plan = new PackPlan([hostile], [], EdgeCategories.Codes.ToDictionary(c => c, _ => false));
        var rows = Perfect(plan);
        rows[0]["notes"] = "a, \"quoted\" note";

        var report = CheckLogReader.Read(Csv(rows, "\r\n", bom: true), EngineText(plan));

        report.Problems.ShouldBeEmpty();
        Line(report, 1).ShouldContain("ok; agree");
    }

    [Theory]
    [InlineData("a trade_key that is not in the pack")]
    [InlineData("a row twice")]
    [InlineData("a file that is not CSV")]
    [InlineData("a missing column")]
    [InlineData("a kind that differs")]
    [InlineData("a row with too few cells")]
    [InlineData("a cell over 2000 characters")]
    [InlineData("an empty log")]
    public void A_log_that_cannot_be_used_is_reported_as_a_problem_with_no_lines(string case_)
    {
        var plan = Plan(3);
        var rows = Perfect(plan);
        var text = Csv(rows);
        switch (case_)
        {
            case "a trade_key that is not in the pack":
                rows[0]["trade_key"] = "ORB|NSE:OTHER|Buy|2025-04-08";
                text = Csv(rows);
                break;
            case "a row twice":
                text = Csv([.. rows, rows[0]]);
                break;
            case "a file that is not CSV":
                text = "seq,trade_key\n1,\"never closed\n";
                break;
            case "a missing column":
                text = text.Replace("seen_stop", "seen_stopp", StringComparison.Ordinal);
                break;
            case "a kind that differs":
                rows[0]["kind"] = "spot";
                text = Csv(rows);
                break;
            case "a row with too few cells":
                text += "1,2\n";
                break;
            case "a cell over 2000 characters":
                rows[0]["notes"] = new string('x', 2001);
                text = Csv(rows);
                break;
            default:
                text = string.Empty;
                break;
        }

        var report = CheckLogReader.Read(text, EngineText(plan));

        report.Problems.ShouldNotBeEmpty(case_);
        report.Lines.ShouldBeEmpty();
        report.Complete.ShouldBeFalse();
    }

    [Fact]
    public void An_owner_text_with_an_escape_sequence_or_a_bidi_override_is_echoed_without_it()
    {
        var plan = Plan(2);
        var rows = Perfect(plan);
        (rows[0]["verdict"], rows[0]["notes"]) = ("not_decidable", "red \u001b[31mALERT\u001b[0m ‮evil​ end");
        (rows[1]["seen_exit_price"], rows[1]["finding"]) = (Add(rows[1]["seen_exit_price"], "1"), "data: \u001b]0;title\u0007 pwned");

        var report = CheckLogReader.Read(Csv(rows), EngineText(plan));

        var text = string.Join('\n', report.Lines);
        text.ShouldContain("red [31mALERT[0m evil end");
        text.ShouldContain("pwned");
        text.ShouldNotContain("\u001b");
        text.ShouldNotContain("‮");
        text.ShouldNotContain("​");
        text.ShouldNotContain("\u0007");
    }

    [Fact]
    public void A_row_of_the_pack_that_is_not_in_the_log_is_reported_and_keeps_the_pack_incomplete()
    {
        var plan = Plan(4);
        var rows = Perfect(plan);
        rows.RemoveAt(2);

        var report = CheckLogReader.Read(Csv(rows), EngineText(plan));

        Line(report, 3).ShouldEndWith(": not in the log");
        report.Complete.ShouldBeFalse();
        report.Lines[^1].ShouldContain("1 rows not in the log");
    }
}
