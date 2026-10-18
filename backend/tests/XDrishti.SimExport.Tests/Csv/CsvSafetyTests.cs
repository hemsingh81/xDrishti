using System.Globalization;
using System.Text;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Csv;

/// <summary>Criterion 3 of P3-04b1: the text cells are cleaned of control and format characters, defused against formulas and quoted as RFC 4180 says.</summary>
public sealed class CsvSafetyTests
{
    public static TheoryData<string, string> Cells => new()
    {
        { "=1+1", "'=1+1" },
        { "+SUM(A1)", "'+SUM(A1)" },
        { "-2+3", "'-2+3" },
        { "@cmd", "'@cmd" },
        { "\tx", "'x" },
        { "\rx", "'x" },
        { "\n=1", "'=1" },
        { "a,b", "a,b" },
        { "say \"hi\"", "say \"hi\"" },
        { "line\nbreak", "linebreak" },
        { "carriage\r\nreturn", "carriagereturn" },
        { string.Empty, string.Empty },
        { "日本株", "日本株" },
        { "NSE:M&M", "NSE:M&M" },
        { "a'b", "a'b" },
        { "\u200B=1+1", "'=1+1" },
        { "\0@x", "'@x" },
        { "\u202E@cmd", "'@cmd" },
        { "\u202Eabc\u202C", "abc" },
        { "ab\u200Bcd\0ef\tgh", "abcdefgh" },
        { "x=1", "x=1" },
        { "\uFEFF+1", "'+1" },
    };

    private static string Cell(ReadOnlySpan<char> text)
    {
        var row = new CsvRow();
        row.Begin();
        row.Text(text);
        row.Raw("end");
        var sink = new System.Buffers.ArrayBufferWriter<byte>();
        row.End(sink);
        return Encoding.UTF8.GetString(sink.WrittenSpan);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void A_text_cell_is_cleaned_defused_and_parses_back_to_the_expected_text(string input, string expected)
    {
        var text = Cell(input);

        Rfc4180Reader.Read(text).Single()[0].ShouldBe(expected);
        text.ShouldEndWith(",end\n");
    }

    [Fact]
    public void A_cell_with_a_comma_or_a_quote_is_quoted_and_its_quotes_doubled()
    {
        Cell("a,b").ShouldBe("\"a,b\",end\n");
        Cell("say \"hi\"").ShouldBe("\"say \"\"hi\"\"\",end\n");
        Cell("=a,b").ShouldBe("\"'=a,b\",end\n");
        Cell("plain").ShouldBe("plain,end\n");
    }

    [Fact]
    public void No_control_or_format_character_is_left_in_the_output()
    {
        var hostile = new StringBuilder();
        for (var c = 0; c < 0x2100; c++)
        {
            if (c is not ('\n' or '\r' or '\t'))
            {
                hostile.Append((char)c);
            }
        }

        var cell = Rfc4180Reader.Read(Cell(hostile.ToString())).Single()[0];

        foreach (var rune in cell.EnumerateRunes())
        {
            Rune.GetUnicodeCategory(rune).ShouldNotBe(UnicodeCategory.Control);
            Rune.GetUnicodeCategory(rune).ShouldNotBe(UnicodeCategory.Format);
        }
    }

    [Fact]
    public void A_format_character_outside_the_basic_plane_is_dropped_too()
    {
        // U+E0041 is a tag character (category Format) written as a surrogate pair
        var tag = char.ConvertFromUtf32(0xE0041);

        Rfc4180Reader.Read(Cell(tag + "=1")).Single()[0].ShouldBe("'=1");
        Rfc4180Reader.Read(Cell("a" + tag + "b")).Single()[0].ShouldBe("ab");
    }

    [Fact]
    public void A_lone_surrogate_becomes_the_replacement_character_and_the_file_is_valid_utf8()
    {
        var row = new CsvRow();
        row.Begin();
        row.Text("a\uD800b");
        var sink = new System.Buffers.ArrayBufferWriter<byte>();
        row.End(sink);

        new UTF8Encoding(false, true).GetString(sink.WrittenSpan).ShouldBe("a�b\n");
    }

    [Fact]
    public void The_hostile_symbol_goes_through_the_whole_file_and_the_reader_gets_the_expected_cells_back()
    {
        var run = FabricatedRun.Standard();
        var hostile = new[] { "=HYPERLINK(\"http://x\",\"y\")", "\u200B+1", "a,b\"c", "-1" };
        var trades = hostile.Select((symbol, i) => new RunTrade(run.Trades[0].Trade with { Symbol = symbol, TradeDate = FabricatedRun.Monday.AddDays(i) }, [])).ToList();

        var rows = Rfc4180Reader.Read(Sinks.Render(s => TradesCsv.Write(s, run.RunKey, trades)));
        var header = rows[0];

        rows.Skip(1).Select(r => r[header.IndexOf("symbol")]).ShouldBe(["'=HYPERLINK(\"http://x\",\"y\")", "'+1", "a,b\"c", "'-1"]);
        rows.Skip(1).ShouldAllBe(r => r.Count == header.Count);
        rows.Skip(1).Select(r => r[header.IndexOf("key")]).ShouldAllBe(k => k.StartsWith("ORB|", StringComparison.Ordinal), "the key cell starts with the strategy and is not a formula");
    }

    [Fact]
    public void A_strategy_id_that_looks_like_a_formula_is_defused_in_its_cell_and_in_the_key()
    {
        var run = FabricatedRun.Standard();
        var trade = run.Trades[0].Trade with { StrategyId = "=cmd" };

        var rows = Rfc4180Reader.Read(Sinks.Render(s => TradesCsv.Write(s, run.RunKey, [new RunTrade(trade, [])])));

        rows[1][rows[0].IndexOf("strategy_id")].ShouldBe("'=cmd");
        rows[1][rows[0].IndexOf("key")].ShouldStartWith("'=cmd|");
    }

    [Fact]
    public void The_independent_reader_refuses_text_a_strict_reader_would_refuse()
    {
        Should.Throw<FormatException>(() => Rfc4180Reader.Read("a,\"b"));
        Should.Throw<FormatException>(() => Rfc4180Reader.Read("a,b\"c\n"));
        Should.Throw<FormatException>(() => Rfc4180Reader.Read("\"a\"b,c\n"));
        Should.Throw<FormatException>(() => Rfc4180Reader.Read("a,b"));
        Rfc4180Reader.Read("\"a\"\"b\",\"c,d\"\n").Single().ShouldBe(["a\"b", "c,d"]);
    }
}
