using System.Text;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class CsvBarReaderTests
{
    private const string Header = "time,open,high,low,close,CE Breakout Level,PE Breakout Level,Volume";

    private const long T0 = 1_711_943_100; // 2024-04-01 09:15 India

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(CsvReadSummary Summary, List<Bar> Bars, List<int> Batches)> ReadAsync(string text, Stream? stream = null)
    {
        var bars = new List<Bar>();
        var batches = new List<int>();
        var summary = await CsvBarReader.ReadAsync(
            stream ?? new MemoryStream(Encoding.UTF8.GetBytes(text)),
            (batch, _) =>
            {
                bars.AddRange(batch.Span.ToArray());
                batches.Add(batch.Length);
                return ValueTask.CompletedTask;
            },
            Ct);
        return (summary, bars, batches);
    }

    private static string Row(long time, string open = "100", string high = "101", string low = "99", string close = "100.5", string volume = "10") => $"{time},{open},{high},{low},{close},,,{volume}";

    [Fact]
    public async Task Rows_become_bars_with_the_numbers_as_written()
    {
        var (summary, bars, _) = await ReadAsync($"{Header}\n{Row(T0, "6422.45", "6475.0", "6397.05", "6452.3", "15773")}\n{Row(T0 + 60, volume: "0")}\n");

        bars.ShouldBe([new Bar(T0, 6422.45, 6475.0, 6397.05, 6452.3, 15773), new Bar(T0 + 60, 100, 101, 99, 100.5, 0)]);
        (summary.Rows, summary.Accepted, summary.Repaired, summary.Rejected, summary.FileError).ShouldBe((2, 2, 0, 0, null));
    }

    [Fact]
    public async Task Columns_are_found_by_name_in_any_order_and_case_and_the_others_are_ignored()
    {
        var text = "Extra,VOLUME, Close ,\"low\",High,open,TIME,tail\r\nx,7,5,1,9,2,1711943100,y\r\n";

        var (summary, bars, _) = await ReadAsync(text);

        bars.ShouldBe([new Bar(T0, 2, 9, 1, 5, 7)]);
        summary.Rejected.ShouldBe(0);
    }

    [Fact]
    public async Task A_byte_order_mark_windows_line_ends_blank_lines_and_a_missing_final_newline_are_fine()
    {
        var text = "﻿" + Header + "\r\n\r\n" + Row(T0) + "\r\n   \r\n" + Row(T0 + 60);

        var (summary, bars, _) = await ReadAsync(text);

        bars.Count.ShouldBe(2);
        (summary.Rows, summary.Rejected).ShouldBe((2, 0));
    }

    [Theory]
    [InlineData("open,high,low,close,volume", "'time'")]
    [InlineData("time,open,high,low,close", "'volume'")]
    [InlineData("a,b,c", "'time', 'open', 'high', 'low', 'close', 'volume'")]
    public async Task A_header_without_a_required_column_makes_the_whole_file_unusable(string header, string missing)
    {
        var (summary, bars, _) = await ReadAsync($"{header}\n1,2,3,4,5,6\n");

        summary.FileError.ShouldBe($"The header has no {missing} column.");
        bars.ShouldBeEmpty();
        summary.Rows.ShouldBe(0);
    }

    [Fact]
    public async Task An_empty_file_has_no_rows_and_is_not_an_error_until_it_lacks_a_header()
    {
        var (summary, _, _) = await ReadAsync(string.Empty);

        summary.FileError.ShouldBeNull();
        summary.Rows.ShouldBe(0);
        (await ReadAsync(Header + "\n")).Summary.Rows.ShouldBe(0);
    }

    [Fact]
    public async Task A_row_that_breaks_a_rule_is_counted_by_reason_with_its_line_number_and_the_rest_goes_on()
    {
        var text = string.Join('\n', Header, Row(T0), Row(T0 + 60, open: "0"), Row(T0 + 120, volume: "-1"), Row(T0 + 181), Row(T0 + 240, open: "x"),
            "1,2,3", Row(T0 + 300, volume: ""), Row(T0, open: "5"), "1711943100000,1,1,1,1,,,1", Row(T0 + 360, close: "NaN"), Row(T0 + 420));

        var (summary, bars, _) = await ReadAsync(text);

        bars.Select(b => b.EpochSeconds).ShouldBe([T0, T0 + 420]);
        (summary.Rows, summary.Accepted, summary.Rejected).ShouldBe((11, 2, 9));
        summary.Rejections.ShouldBe(
            new Dictionary<string, long>
            {
                [CsvBarReader.NonPositivePrice] = 1,
                [CsvBarReader.NegativeVolume] = 1,
                [CsvBarReader.NotWholeMinute] = 1,
                [CsvBarReader.Unparseable] = 3,
                [CsvBarReader.Duplicate] = 1,
                [CsvBarReader.TimeOutOfRange] = 1,
                [CsvBarReader.NotANumber] = 1,
            },
            ignoreOrder: true);
        summary.Examples[0].ShouldBe("line 3: a price is not above zero");
        summary.Examples.Count.ShouldBe(9);
    }

    [Fact]
    public async Task Only_the_first_examples_of_rejected_rows_are_kept_but_every_rejection_is_counted()
    {
        var text = Header + "\n" + string.Join('\n', Enumerable.Range(0, 25).Select(i => Row(T0 + i * 60L, open: "0")));

        var (summary, _, _) = await ReadAsync(text);

        summary.Rejected.ShouldBe(25);
        summary.Rejections[CsvBarReader.NonPositivePrice].ShouldBe(25);
        summary.Examples.Count.ShouldBe(CsvPhase.MaxExamples);
        summary.Examples[^1].ShouldBe("line 11: a price is not above zero");
    }

    [Fact]
    public async Task The_first_of_two_rows_for_the_same_minute_is_kept()
    {
        var (summary, bars, _) = await ReadAsync($"{Header}\n{Row(T0, close: "100.5")}\n{Row(T0, close: "7", low: "1")}\n");

        bars.ShouldHaveSingleItem().Close.ShouldBe(100.5);
        summary.Rejections[CsvBarReader.Duplicate].ShouldBe(1);
    }

    [Fact]
    public async Task A_bar_whose_high_and_low_do_not_contain_open_and_close_is_repaired_and_counted()
    {
        var (summary, bars, _) = await ReadAsync($"{Header}\n{Row(T0, open: "110")}\n{Row(T0 + 60)}\n");

        bars[0].ShouldBe(new Bar(T0, 110, 110, 99, 100.5, 10));
        (summary.Accepted, summary.Repaired, summary.Rejected).ShouldBe((2, 1, 0));
    }

    [Theory]
    [InlineData("15773.0", 15773)]
    [InlineData("1e3", 1000)]
    [InlineData("0", 0)]
    public async Task A_whole_volume_written_as_a_decimal_is_accepted(string volume, long expected)
    {
        var (_, bars, _) = await ReadAsync($"{Header}\n{Row(T0, volume: volume)}\n");

        bars.ShouldHaveSingleItem().Volume.ShouldBe(expected);
    }

    [Fact]
    public async Task A_fractional_volume_is_rejected()
    {
        var (summary, bars, _) = await ReadAsync($"{Header}\n{Row(T0, volume: "1.5")}\n");

        bars.ShouldBeEmpty();
        summary.Rejections[CsvBarReader.Unparseable].ShouldBe(1);
    }

    [Fact]
    public async Task A_big_file_is_handed_over_in_batches_and_only_valid_rows_are_in_them()
    {
        const int Count = CsvBarReader.BatchSize * 2 + 100;
        var text = new StringBuilder(Header).Append('\n');
        for (var i = 0; i < Count; i++)
        {
            text.Append(Row(T0 + (i / 375 * 86_400L) + (i % 375 * 60L))).Append('\n'); // whole sessions, one after the other
        }

        var (summary, bars, batches) = await ReadAsync(text.ToString());

        batches.ShouldBe([CsvBarReader.BatchSize, CsvBarReader.BatchSize, 100]);
        bars.Count.ShouldBe(Count);
        bars.Select(b => b.EpochSeconds).ShouldBeInOrder();
        summary.Accepted.ShouldBe(Count);
    }

    [Fact]
    public async Task A_stream_that_hands_out_a_few_bytes_at_a_time_reads_the_same()
    {
        var text = $"{Header}\n{Row(T0)}\n{Row(T0 + 60)}\n{Row(T0 + 120)}";

        var (summary, bars, _) = await ReadAsync(text, new TrickleStream(Encoding.UTF8.GetBytes(text), 7));

        bars.Count.ShouldBe(3);
        summary.Rejected.ShouldBe(0);
    }

    [Fact]
    public async Task A_line_longer_than_a_bar_can_be_ends_the_file_with_an_error_wherever_it_is()
    {
        var long1 = new string('x', CsvBarReader.MaxLineBytes + 10);

        var (inMemory, _, _) = await ReadAsync($"{Header}\n{Row(T0)}\n{long1}\n{Row(T0 + 60)}\n");
        var (trickled, _, _) = await ReadAsync(long1, new TrickleStream(Encoding.UTF8.GetBytes(Header + "\n" + long1), 100));

        inMemory.FileError.ShouldBe($"Line 3 is longer than {CsvBarReader.MaxLineBytes} bytes: this is not a file of bars.");
        trickled.FileError.ShouldBe($"Line 2 is longer than {CsvBarReader.MaxLineBytes} bytes: this is not a file of bars.");
    }

    [Fact]
    public async Task Cancelling_stops_the_read()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await CsvBarReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(Header + "\n")), (_, _) => ValueTask.CompletedTask, cancel.Token));
    }

    private sealed class TrickleStream(byte[] content, int chunk) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, chunk), content.Length - _position);
            Array.Copy(content, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = Math.Min(Math.Min(buffer.Length, chunk), content.Length - _position);
            content.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return ValueTask.FromResult(n);
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
