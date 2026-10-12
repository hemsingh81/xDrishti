using System.Text;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>What makes a whole CSV file unusable (nothing of it may be stored): a wrong clock, the wrong time unit, the future, and the limits that bound the work per file.</summary>
public sealed class CsvFileChecksTests
{
    private const long Day0 = CsvFixtures.Day0;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CsvReadSummary> ReadAsync(string text, CsvReadLimits? limits = null) =>
        await CsvBarReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), (_, _) => ValueTask.CompletedTask, Ct, limits);

    [Theory]
    [InlineData(19_800, "subtracting")]
    [InlineData(-19_800, "adding")]
    public async Task A_file_off_by_the_india_utc_offset_fails_and_names_the_shift(int shiftSeconds, string wording)
    {
        var summary = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 3, shiftSeconds: shiftSeconds)));

        summary.FileError.ShouldNotBeNull();
        summary.FileError.ShouldContain($"after {wording} 5 h 30 min");
        summary.FileError.ShouldContain("India/UTC offset");
        summary.FileError.ShouldContain("nothing was shifted or stored");
    }

    [Fact]
    public async Task A_file_stamped_with_the_end_of_each_minute_fails()
    {
        var summary = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 3, firstMinute: 1)));

        summary.FileError.ShouldNotBeNull().ShouldContain("END of each minute");
    }

    [Fact]
    public async Task A_file_whose_times_are_in_no_session_and_fit_no_shift_fails()
    {
        var summary = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 3, count: 120, firstMinute: 765)));  // 22:00 to 23:59 India time

        summary.FileError.ShouldNotBeNull().ShouldContain("outside every trading session");
    }

    [Fact]
    public async Task A_good_file_with_the_muhurat_evening_session_a_saturday_session_and_a_few_stray_minutes_loads()
    {
        var muhurat = CsvFixtures.Times(days: 1, count: 60, firstMinute: 540); // 18:15 to 19:14
        var stray = new[] { Day0 + (CsvFixtures.Day * 3) + (765 * 60L), Day0 + (CsvFixtures.Day * 3) + (375 * 60L) }; // 22:00 and 15:30
        var times = CsvFixtures.Times(days: 6).Concat(muhurat).Concat(stray);

        var summary = await ReadAsync(CsvFixtures.Csv(times));

        summary.FileError.ShouldBeNull();
        summary.Accepted.ShouldBe((6 * 375) + 60 + 2);
    }

    [Fact]
    public async Task A_file_with_only_a_special_evening_session_loads()
    {
        var summary = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 1, count: 60, firstMinute: 540)));

        summary.FileError.ShouldBeNull();
        summary.Accepted.ShouldBe(60);
    }

    [Fact]
    public async Task A_file_in_milliseconds_fails_instead_of_loading_zero_bars()
    {
        var summary = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 2).Select(t => t * 1000)));

        summary.FileError.ShouldNotBeNull().ShouldContain("look like epoch milliseconds");
        summary.Rejections[CsvBarReader.TimeOutOfRange].ShouldBe(750);
        summary.Accepted.ShouldBe(0);
    }

    [Fact]
    public async Task A_few_millisecond_rows_in_a_good_file_are_only_rejected()
    {
        var summary = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 2).Append(Day0 * 1000)));

        summary.FileError.ShouldBeNull();
        summary.Rejections[CsvBarReader.TimeOutOfRange].ShouldBe(1);
    }

    [Fact]
    public async Task A_file_dated_after_tomorrow_fails_and_a_few_future_rows_are_only_rejected()
    {
        var limits = CsvReadLimits.Default with { LatestEpochSeconds = Day0 + CsvFixtures.Day };
        var inTheFuture = CsvFixtures.Times(days: 2, startDay: 30);

        var allFuture = await ReadAsync(CsvFixtures.Csv(inTheFuture), limits);
        var fewFuture = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 1).Concat(inTheFuture.Take(3))), limits);

        allFuture.FileError.ShouldNotBeNull().ShouldContain("later than one day from now (2024-04-02)");
        allFuture.Rejections[CsvBarReader.TimeInFuture].ShouldBe(750);
        fewFuture.FileError.ShouldBeNull();
        (fewFuture.Accepted, fewFuture.Rejections[CsvBarReader.TimeInFuture]).ShouldBe((375, 3));
    }

    [Fact]
    public async Task A_time_exactly_one_day_ahead_is_still_fine()
    {
        var limits = CsvReadLimits.Default with { LatestEpochSeconds = Day0 + CsvFixtures.Day };

        var summary = await ReadAsync(CsvFixtures.Csv([Day0 + CsvFixtures.Day, Day0 + CsvFixtures.Day + 60]), limits);

        (summary.Accepted, summary.Rejected).ShouldBe((1, 1));
    }

    [Fact]
    public async Task A_file_with_more_rows_than_allowed_fails_and_one_with_exactly_that_many_loads()
    {
        var limits = CsvReadLimits.Default with { MaxRows = 10 };

        var over = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 1, count: 11)), limits);
        var exactly = await ReadAsync(CsvFixtures.Csv(CsvFixtures.Times(days: 1, count: 10)), limits);

        over.FileError.ShouldBe("The file has more than 10 rows (Data:Csv:MaxRowsPerFile): this is not a normal file of bars.");
        exactly.FileError.ShouldBeNull();
    }

    [Fact]
    public async Task A_file_spanning_more_than_the_allowed_time_fails_and_one_spanning_exactly_that_loads()
    {
        var limits = CsvReadLimits.Default with { MaxSpan = TimeSpan.FromDays(30) };

        var over = await ReadAsync(CsvFixtures.Csv([Day0, Day0 + (40 * CsvFixtures.Day)]), limits);
        var exactly = await ReadAsync(CsvFixtures.Csv([Day0, Day0 + (30 * CsvFixtures.Day)]), limits);

        over.FileError.ShouldBe("The file spans more than 30 days (from 2024-04-01 on, Data:Csv:MaxSpanDays): this is not a normal file of bars.");
        exactly.FileError.ShouldBeNull();
    }

    [Fact]
    public async Task The_default_limits_allow_about_ten_years_which_is_a_hundred_and_twenty_monthly_chunks()
    {
        CsvReadLimits.Default.MaxSpan.TotalDays.ShouldBe(3_700);
        var tenYears = await ReadAsync(CsvFixtures.Csv([Day0, Day0 + (3_650 * CsvFixtures.Day)]));
        var twelveYears = await ReadAsync(CsvFixtures.Csv([Day0, Day0 + (4_400 * CsvFixtures.Day)]));

        tenYears.FileError.ShouldBeNull();
        twelveYears.FileError.ShouldNotBeNull().ShouldContain("spans more than 3,700 days");
    }

    [Fact]
    public async Task A_stream_that_never_ends_stops_at_the_byte_limit()
    {
        var limits = CsvReadLimits.Default with { MaxBytes = 1 << 20, MaxRows = long.MaxValue };

        var summary = await CsvBarReader.ReadAsync(new EndlessBars(), (_, _) => ValueTask.CompletedTask, Ct, limits);

        summary.FileError.ShouldBe("The file is larger than 1,048,576 bytes once decompressed (Data:Csv:MaxBytesPerFile): this is not a normal file of bars.");
        summary.Rows.ShouldBeLessThan(40_000);
    }

    [Fact]
    public void The_streaming_probe_finds_nothing_in_an_empty_file()
    {
        new CsvSessionProbe().Problem().ShouldBeNull();
    }

    [Fact]
    public void The_streaming_probe_agrees_with_the_window_check_on_a_shifted_day()
    {
        var probe = new CsvSessionProbe();
        foreach (var time in CsvFixtures.Times(days: 1, shiftSeconds: 19_800))
        {
            probe.Add(time);
        }

        var window = new FetchWindow(DateTimeOffset.FromUnixTimeSeconds(Day0 - 3600), DateTimeOffset.FromUnixTimeSeconds(Day0 + 86_400));
        var bars = CsvFixtures.Times(days: 1, shiftSeconds: 19_800).Select(t => new Bar(t, 100, 101, 99, 100.5, 10)).ToList();

        probe.Problem().ShouldNotBeNull().ShouldContain("India/UTC offset");
        IntradayAlignmentCheck.Problem(bars, window).ShouldNotBeNull().ShouldContain("India/UTC offset");
    }

    /// <summary>Valid rows for ever, one minute apart: what a compressed bomb looks like once decompressed.</summary>
    private sealed class EndlessBars : Stream
    {
        private byte[] _pending = Encoding.UTF8.GetBytes(CsvFixtures.Header + "\n");
        private int _offset;
        private long _time = Day0;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Fill(buffer.Span));

        private int Fill(Span<byte> target)
        {
            var written = 0;
            while (written < target.Length)
            {
                if (_offset == _pending.Length)
                {
                    _pending = Encoding.UTF8.GetBytes(CsvFixtures.Row(_time) + "\n");
                    _time += 60;
                    _offset = 0;
                }

                var n = Math.Min(target.Length - written, _pending.Length - _offset);
                _pending.AsSpan(_offset, n).CopyTo(target[written..]);
                _offset += n;
                written += n;
            }

            return written;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
