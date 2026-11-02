using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-04: the small RFC 4180 reader (BOM, LF and CRLF, quoting, a line break inside quotes, bad quotes, empty lines).</summary>
public sealed class CsvTableTests
{
    private static string[] Fields(CsvRecord r) => [.. r.Fields!];

    [Fact]
    public void Lf_and_crlf_files_give_the_same_records_with_their_line_numbers_and_empty_lines_are_skipped()
    {
        var lf = CsvTable.Parse("a,b\n\n1,2\n3,4\n");
        var crlf = CsvTable.Parse("a,b\r\n\r\n1,2\r\n3,4");

        lf.Select(r => (r.Line, string.Join("|", r.Fields!))).ShouldBe([(1, "a|b"), (3, "1|2"), (4, "3|4")]);
        crlf.Select(r => (r.Line, string.Join("|", r.Fields!))).ShouldBe(lf.Select(r => (r.Line, string.Join("|", r.Fields!))));
    }

    [Fact]
    public void A_leading_bom_is_accepted()
    {
        var records = CsvTable.Parse("﻿symbol,x\nA,B");

        Fields(records[0]).ShouldBe(["symbol", "x"]);
    }

    [Fact]
    public void Quoted_fields_hold_commas_doubled_quotes_and_line_breaks_and_the_next_record_keeps_the_right_line()
    {
        var records = CsvTable.Parse("a,\"b,c\",\"say \"\"hi\"\"\"\n\"two\nlines\",x,\n9,9,9");

        Fields(records[0]).ShouldBe(["a", "b,c", "say \"hi\""]);
        Fields(records[1]).ShouldBe(["two\nlines", "x", ""]);
        records[1].Line.ShouldBe(2);
        records[2].Line.ShouldBe(4);
    }

    [Theory]
    [InlineData("a,b\"c,d\n1,2,3")]
    [InlineData("a,\"b\"c,d\n1,2,3")]
    public void A_bad_quote_marks_that_record_only(string text)
    {
        var records = CsvTable.Parse(text);

        records[0].Fields.ShouldBeNull();
        Fields(records[1]).ShouldBe(["1", "2", "3"]);
    }

    [Fact]
    public void An_unclosed_quote_marks_its_record_and_swallows_the_rest()
    {
        var records = CsvTable.Parse("a,b\n1,\"2\n3,4");

        records.Count.ShouldBe(2);
        records[1].Fields.ShouldBeNull();
    }

    [Fact]
    public void A_line_holding_only_an_empty_quoted_field_is_a_record_and_empty_input_has_none()
    {
        CsvTable.Parse("\"\"\n").Count.ShouldBe(1);
        CsvTable.Parse(string.Empty).ShouldBeEmpty();
        CsvTable.Parse("\n\n").ShouldBeEmpty();
    }
}
