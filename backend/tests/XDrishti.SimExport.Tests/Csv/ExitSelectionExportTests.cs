using System.Buffers;
using System.Text;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Text;

namespace XDrishti.SimExport.Tests.Csv;

/// <summary>Criterion 8 of P4-06: the record, the CSV and the summary carry the inputs, the folds, the per-fold tables, the verdict and the in-sample line, and the same inputs give the same bytes.</summary>
public sealed class ExitSelectionExportTests
{
    private static ExitSelection Selection(int seed = 1)
    {
        var random = new Random(seed);
        ExitCandidate[] candidates = [Candidate(2), Candidate(3)];
        var sessions = new List<DateOnly>();
        for (var d = new DateOnly(2024, 4, 1); d <= new DateOnly(2025, 12, 31); d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(d);
            }
        }

        var trades = sessions.SelectMany(d => candidates.Select((c, i) => new SelectionTrade(c.Id, d, d, ((i == 1 ? 0.4 : 0.0) + random.NextDouble()) - 0.5))).ToList();
        return ExitSelector.Select(new SelectionInput("ORB 15m BUY intraday *", "RFilled", false, new string('a', 64), candidates, trades, sessions, new SelectionOptions()));
    }

    private static ExitCandidate Candidate(double rr) => new("FIXED_RR", [new ExitParameterValue("rr", [rr])]);

    private static string Csv(ExitSelection selection)
    {
        var sink = new ArrayBufferWriter<byte>();
        ExitSelectionCsv.Write(sink, selection);
        return new UTF8Encoding(false, true).GetString(sink.WrittenSpan);
    }

    private static string Json(ExitSelection selection)
    {
        var sink = new ArrayBufferWriter<byte>();
        ExitSelectionJson.Write(sink, selection, "xd-sim-export-4");
        return new UTF8Encoding(false, true).GetString(sink.WrittenSpan);
    }

    [Fact]
    public void Every_presentation_carries_the_in_sample_line()
    {
        var selection = Selection();

        ExitSelectionText.Summary(selection).ShouldContain(ExitSelection.InSampleLine(selection.FirstEntry, selection.LastEntry));
        Json(selection).ShouldContain("in-sample on the data shown");
        var lines = Csv(selection).TrimEnd('\n').Split('\n');
        lines.Length.ShouldBeGreaterThan(2);
        lines.Skip(1).ShouldAllBe(l => l.Contains("in-sample on the data shown", StringComparison.Ordinal));
    }

    [Fact]
    public void A_record_without_the_line_fails_the_check()
    {
        var selection = Selection() with { Banner = [] };

        ExitSelectionText.Summary(selection).Contains(ExitSelection.InSampleLine(selection.FirstEntry, selection.LastEntry), StringComparison.Ordinal).ShouldBeFalse("the summary prints the banner of the record: remove the line and it is gone");
        Json(selection).Contains("in-sample on the data shown", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void The_csv_has_a_row_for_each_fold_and_candidate_and_four_decimals()
    {
        var selection = Selection();

        var lines = Csv(selection).TrimEnd('\n').Split('\n');

        lines[0].ShouldBe(string.Join(',', ExitSelectionCsv.Columns));
        lines.Length.ShouldBe(1 + (selection.Folds.Count * 2));
        lines[1].ShouldStartWith("1,2025-01-01,2025-03-31,");
        lines[1].ShouldContain("FIXED_RR{rr=2}");
        lines[1].ShouldMatch(@",-?\d+\.\d{4},");
    }

    [Fact]
    public void The_record_holds_the_inputs_the_folds_the_verdict_and_the_chance_of_passing_by_luck()
    {
        var json = Json(Selection());

        json.ShouldStartWith("{\"format\":\"xd-sim-export-4\",\"schema\":1,");
        json.ShouldContain("\"inputs\":{\"min_train_months\":9,\"test_months\":3,\"embargo_sessions\":5,\"lambda\":0.500000,\"min_trades\":30,\"prefer_plateau\":true,\"stable_folds_pct\":60.000000}");
        json.ShouldContain("\"folds\":[{\"index\":1,");
        json.ShouldContain("\"stability\":{");
        json.ShouldContain("\"chance_pass\":");
        json.ShouldContain("can pass by chance about");
    }

    [Fact]
    public void The_same_inputs_give_the_same_bytes()
    {
        Json(Selection(4)).ShouldBe(Json(Selection(4)));
        Csv(Selection(4)).ShouldBe(Csv(Selection(4)));
        ExitSelectionText.Summary(Selection(4)).ShouldBe(ExitSelectionText.Summary(Selection(4)));
    }

    [Fact]
    public void A_run_without_a_fold_writes_one_row_and_still_says_what_it_is()
    {
        var empty = Selection() with { Folds = [] };

        var lines = Csv(empty).TrimEnd('\n').Split('\n');

        lines.Length.ShouldBe(2);
        lines[1].ShouldStartWith("none,");
        lines[1].ShouldContain("in-sample on the data shown");
    }
}
