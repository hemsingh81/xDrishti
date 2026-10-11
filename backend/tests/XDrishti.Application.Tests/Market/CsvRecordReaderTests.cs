using XDrishti.Application.Market.Import;

namespace XDrishti.Application.Tests.Market;

public sealed class CsvRecordReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string[]> FieldsAsync(string text)
    {
        var csv = new CsvRecordReader(new StringReader(text));
        return await csv.SplitAsync((await csv.ReadLineAsync(Ct))!, Ct);
    }

    [Fact]
    public async Task Plain_fields_and_empty_fields_split_on_commas() =>
        (await FieldsAsync("a,,c,")).ShouldBe(["a", "", "c", ""]);

    [Fact]
    public async Task Quotes_protect_commas_and_double_quotes_escape_a_quote() =>
        (await FieldsAsync("\"a,b\",\"say \"\"hi\"\"\",c")).ShouldBe(["a,b", "say \"hi\"", "c"]);

    [Fact]
    public async Task A_quoted_field_may_run_over_a_line_break() =>
        (await FieldsAsync("a,\"line1\nline2\",c\nnext")).ShouldBe(["a", "line1\nline2", "c"]);

    [Fact]
    public async Task A_quote_that_never_closes_is_an_error() =>
        await Should.ThrowAsync<FormatException>(() => FieldsAsync("a,\"open"));

    [Fact]
    public async Task Line_numbers_count_physical_lines()
    {
        var csv = new CsvRecordReader(new StringReader("h\n\"x\ny\"\nz\n"));
        await csv.ReadLineAsync(Ct);
        await csv.SplitAsync((await csv.ReadLineAsync(Ct))!, Ct);
        await csv.ReadLineAsync(Ct);

        csv.RecordNumber.ShouldBe(4);
    }

    [Fact]
    public async Task Lines_end_at_LF_CRLF_or_CR_even_when_the_terminator_is_split_across_reads()
    {
        var text = "a,b\r\nc,d\re,f\n\ng\r\n" + new string('x', 16 * 1024 - 2) + "\r\nlast";
        var csv = new CsvRecordReader(new SlowReader(text));
        var lines = new List<string?>();
        while (await csv.ReadLineAsync(Ct) is { } line)
        {
            lines.Add(line);
        }

        lines.ShouldBe(["a,b", "c,d", "e,f", "", "g", new string('x', 16 * 1024 - 2), "last"]);
    }

    [Fact]
    public async Task A_line_longer_than_the_cap_is_refused_before_it_is_held()
    {
        var csv = new CsvRecordReader(new StringReader(new string('x', CsvRecordReader.MaxRecordLength + 1) + "\nnext"));

        await Should.ThrowAsync<FormatException>(async () => await csv.ReadLineAsync(Ct));
    }

    [Fact]
    public async Task A_line_exactly_at_the_cap_is_fine()
    {
        var csv = new CsvRecordReader(new StringReader(new string('x', CsvRecordReader.MaxRecordLength) + "\nnext"));

        (await csv.ReadLineAsync(Ct))!.Length.ShouldBe(CsvRecordReader.MaxRecordLength);
        (await csv.ReadLineAsync(Ct)).ShouldBe("next");
    }

    [Fact]
    public async Task A_quoted_field_that_keeps_running_over_lines_hits_the_cap()
    {
        var text = "\"" + string.Join("\n", Enumerable.Repeat(new string('y', 10_000), 10));
        var csv = new CsvRecordReader(new StringReader(text));

        await Should.ThrowAsync<FormatException>(async () => await csv.SplitAsync((await csv.ReadLineAsync(Ct))!, Ct));
    }

    /// <summary>Hands out the text in small pieces, like a network stream does.</summary>
    private sealed class SlowReader(string text) : TextReader
    {
        private int _position;

        public override int Read(char[] buffer, int index, int count)
        {
            var n = Math.Min(Math.Min(count, 7), text.Length - _position);
            text.CopyTo(_position, buffer, index, n);
            _position += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            var n = Math.Min(Math.Min(buffer.Length, 7), text.Length - _position);
            text.AsSpan(_position, n).CopyTo(buffer.Span);
            _position += n;
            return ValueTask.FromResult(n);
        }
    }
}
