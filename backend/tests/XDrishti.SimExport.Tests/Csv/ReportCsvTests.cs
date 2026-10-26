using System.Buffers;
using System.Text;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Csv;

/// <summary>Spec P8-20 (criteria 3, 4, 5, 12): the body writer, the evidence comment lines and the formula-injection hardening, on hand-worked fixtures and the OWASP corpus.</summary>
public sealed class ReportCsvTests
{
    private static readonly string[] Corpus =
    [
        "=cmd|' /C calc'!A0", "@SUM(1+1)*cmd|' /C calc'!A0", "=HYPERLINK(\"http://e\",\"x\")", "+1+1", "-2+3", "\t=1", "＝1+1", " =1", "  \t@x", "＋1", "－1", "＠SUM(1)", " =1", "​ =1", "\r=1", "\n+1", "a,=b", "\"=1\"",
    ];

    private static string Text(IBufferWriter<byte> sink) => Encoding.UTF8.GetString(((ArrayBufferWriter<byte>)sink).WrittenSpan);

    private static bool Trigger(char c) => c is '=' or '+' or '-' or '@' or '＝' or '＋' or '－' or '＠';

    [Theory]
    [InlineData("＝1+1", "'＝1+1")]
    [InlineData("＋1", "'＋1")]
    [InlineData("－1", "'－1")]
    [InlineData("＠x", "'＠x")]
    [InlineData(" =1", "' =1")]
    [InlineData("   -x", "'   -x")]
    [InlineData(" @x", "' @x")]
    [InlineData("\t =1", "' =1")]
    [InlineData("a =1", "a =1")]
    [InlineData(" x", " x")]
    [InlineData("   ", "   ")]
    public void The_new_triggers_and_a_leading_space_are_defused_and_a_plain_text_is_not(string input, string expected)
    {
        var row = new CsvRow();
        row.Begin();
        row.Text(input);
        var sink = new ArrayBufferWriter<byte>();
        row.End(sink);

        Rfc4180Reader.Read(Text(sink)).Single()[0].ShouldBe(expected);
    }

    [Fact]
    public void The_hand_worked_table_is_written_byte_for_byte_and_a_non_value_has_an_empty_number_cell()
    {
        var sink = new ArrayBufferWriter<byte>();
        var body = new ReportCsvBody(sink, ["group", "n", "expectancy_r", "expectancy_r_state", "expectancy_r_reason"]);
        body.Add([ExportCell.OfText("ORB|15m"), ExportCell.OfWhole(40), ExportCell.OfReal(0.12), ExportCell.OfText("value"), ExportCell.Empty]);
        body.Add([ExportCell.OfText("VWAP|5m"), ExportCell.OfWhole(9), ExportCell.Empty, ExportCell.OfText("insufficient"), ExportCell.OfText("n 9 of at least 15")]);
        body.Add([ExportCell.OfText("BASE|1D"), ExportCell.OfWhole(0), ExportCell.Empty, ExportCell.OfText("unavailable"), ExportCell.OfText("exit not recorded for run abc")]);

        Text(sink).ShouldBe("group,n,expectancy_r,expectancy_r_state,expectancy_r_reason\nORB|15m,40,0.12,value,\nVWAP|5m,9,,insufficient,n 9 of at least 15\nBASE|1D,0,,unavailable,exit not recorded for run abc\n");
        body.Rows.ShouldBe(3);
    }

    [Fact]
    public void The_same_rows_give_the_same_bytes_and_a_row_of_the_wrong_width_is_refused()
    {
        byte[] Make()
        {
            var sink = new ArrayBufferWriter<byte>();
            var body = new ReportCsvBody(sink, ["a", "b"]);
            body.Add([ExportCell.OfText("x"), ExportCell.OfReal(-1.5)]);
            return sink.WrittenSpan.ToArray();
        }

        Make().ShouldBe(Make());
        var bad = new ReportCsvBody(new ArrayBufferWriter<byte>(), ["a", "b"]);
        Should.Throw<ArgumentException>(() => bad.Add([ExportCell.Empty]));
    }

    [Fact]
    public void A_number_is_written_only_from_a_numeric_type_and_a_non_finite_number_is_an_empty_cell()
    {
        var sink = new ArrayBufferWriter<byte>();
        var body = new ReportCsvBody(sink, ["a", "b", "c", "d"]);
        body.Add([ExportCell.OfReal(double.NaN), ExportCell.OfReal(double.PositiveInfinity), ExportCell.OfReal(-0.5), ExportCell.OfWhole(-3)]);

        Text(sink).ShouldBe("a,b,c,d\n,,-0.5,-3\n");
    }

    [Fact]
    public void Every_corpus_string_in_every_text_column_parses_back_to_a_cell_that_does_not_start_with_a_trigger()
    {
        var columns = Enumerable.Range(0, Corpus.Length).Select(i => "c" + i).ToList();
        var sink = new ArrayBufferWriter<byte>();
        var body = new ReportCsvBody(sink, columns);
        for (var shift = 0; shift < Corpus.Length; shift++)
        {
            body.Add([.. Corpus.Select((_, i) => ExportCell.OfText(Corpus[(i + shift) % Corpus.Length]))]);
        }

        var rows = Rfc4180Reader.Read(Text(sink));

        rows.Count.ShouldBe(1 + Corpus.Length);
        foreach (var row in rows.Skip(1))
        {
            row.Count.ShouldBe(Corpus.Length);
            foreach (var cell in row)
            {
                var first = cell.TrimStart(' ', '\t', ' ', '​').FirstOrDefault();
                (cell.Length == 0 || !Trigger(cell[0])).ShouldBeTrue("starts with a trigger: " + cell);
                (cell.TrimStart().Length == 0 || !Trigger(cell.TrimStart()[0]) || cell[0] == '\'').ShouldBeTrue("hides a trigger behind space: " + cell);
                first.ShouldNotBe('\0');
            }
        }
    }

    [Fact]
    public void An_evidence_line_cannot_end_the_comment_start_a_data_row_or_put_a_formula_in_a_second_cell()
    {
        var sink = new ArrayBufferWriter<byte>();
        ReportCsvEvidence.WriteLines(sink, ["limit: plain", "limit: \n=cmd|' /C calc'!A0", "run: \r\n1,2,3", "x: a,=b \"q\" \t=1 ‮ ​ end", "tab\tsplit"]);
        ReportCsvEvidence.WriteEnd(sink, 7);

        var text = Text(sink);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(6, "five lines and the closing line, however hostile the strings");
        lines.ShouldAllBe(l => l.StartsWith("# ", StringComparison.Ordinal));
        lines.Last().ShouldBe("# end of export, rows 7");
        text.ShouldNotContain("\r");
        Rfc4180Reader.Read(text).ShouldAllBe(row => row[0].StartsWith('#'));
        Rfc4180Reader.Read(text).SkipLast(1).ShouldAllBe(row => row.Count == 1, "a line is one cell; only the closing line the spec words with a comma is two");
        lines[1].ShouldBe("# limit:  =cmd|' /C calc'!A0");
        lines[3].ShouldContain("a;=b 'q'");
    }

    [Fact]
    public void The_cleaner_replaces_and_never_drops_so_that_words_do_not_join()
    {
        ReportCsvEvidence.Clean("a​b\0c\td\ne").ShouldBe("a b c d e");
        ReportCsvEvidence.Clean(null).ShouldBe(string.Empty);
        ReportCsvEvidence.Clean("日本株 ₹").ShouldBe("日本株 ₹");
    }
}
