using System.Buffers;
using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Csv;

/// <summary>
/// Reads the 1-minute CSV format of ADR 0016 §4 as a stream: <c>time</c> (epoch seconds, UTC, bar start), <c>open</c>, <c>high</c>, <c>low</c>, <c>close</c>, <c>volume</c>,
/// found by header name (case-insensitive, any order; other columns ignored). Valid rows go to the sink in batches, so a file of any size needs a few
/// kilobytes of memory. Row rules (<see cref="BarRules"/>): inconsistent high/low are repaired, unparseable rows, non-positive prices, negative volumes and
/// repeated minutes are rejected and counted; zero volume is kept. A time later than one day from now is rejected as <see cref="TimeInFuture"/>. The file as a whole
/// fails (nothing may be stored) when it breaks a <see cref="CsvReadLimits"/> (rows, bytes, span), when most of its times are not epoch seconds in range (milliseconds,
/// the future) or when the <see cref="CsvSessionProbe"/> finds the times off the trading sessions (a 5 h 30 min shift, end-of-minute stamps).
/// </summary>
internal static class CsvBarReader
{
    public const int BatchSize = 8192;

    /// <summary>A line longer than this is not a bar: the file is not what it claims to be.</summary>
    public const int MaxLineBytes = 4096;

    public const string Unparseable = "unparseable";
    public const string TimeOutOfRange = "timeOutOfRange";
    public const string TimeInFuture = "timeInFuture";
    public const string NotWholeMinute = "notWholeMinute";
    public const string NotANumber = "notANumber";
    public const string NonPositivePrice = "nonPositivePrice";
    public const string NegativeVolume = "negativeVolume";
    public const string Duplicate = "duplicateMinute";

    private const int BufferSize = 1 << 16;

    /// <summary>2000-01-01 and 2100-01-01 as epoch seconds: anything else is not a date in seconds (milliseconds, for one).</summary>
    private const long MinEpoch = 946_684_800;

    private const long MaxEpoch = 4_102_444_800;

    /// <summary>The same range in milliseconds: a time in it is almost certainly a millisecond epoch.</summary>
    private const long MinMillis = MinEpoch * 1000;

    private const long MaxMillis = MaxEpoch * 1000;

    private static readonly string[] Required = ["time", "open", "high", "low", "close", "volume"];

    /// <summary>Reads the whole stream. The sink must be done with the batch memory when it returns; batches already handed over must be discarded when the summary has a file error.</summary>
    public static async ValueTask<CsvReadSummary> ReadAsync(
        Stream stream, Func<ReadOnlyMemory<Bar>, CancellationToken, ValueTask> sink, CancellationToken cancellationToken, CsvReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sink);
        limits ??= CsvReadLimits.Default;
        var reader = new LineReader(limits);
        long bytesRead = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int start = 0, end = 0;
            var endOfFile = false;
            while (!reader.Halted)
            {
                var newline = Array.IndexOf(buffer, (byte)'\n', start, end - start);
                if (newline < 0)
                {
                    if (endOfFile)
                    {
                        if (end > start)
                        {
                            reader.Line(buffer, start, end - start);
                        }

                        break;
                    }

                    if (end - start >= MaxLineBytes)
                    {
                        reader.Halt($"Line {reader.LineNumber + 1} is longer than {MaxLineBytes} bytes: this is not a file of bars.");
                        break;
                    }

                    if (start > 0)
                    {
                        Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                        end -= start;
                        start = 0;
                    }

                    var read = await stream.ReadAsync(buffer.AsMemory(end), cancellationToken);
                    if (read == 0)
                    {
                        endOfFile = true;
                    }

                    end += read;
                    bytesRead += read;
                    if (bytesRead > limits.MaxBytes)
                    {
                        reader.Halt(string.Create(CultureInfo.InvariantCulture, $"The file is larger than {limits.MaxBytes:N0} bytes once decompressed (Data:Csv:MaxBytesPerFile): this is not a normal file of bars."));
                        break;
                    }

                    continue;
                }

                if (newline - start > MaxLineBytes)
                {
                    reader.Halt($"Line {reader.LineNumber + 1} is longer than {MaxLineBytes} bytes: this is not a file of bars.");
                    break;
                }

                reader.Line(buffer, start, newline - start);
                start = newline + 1;
                if (reader.BatchFull)
                {
                    await sink(reader.TakeBatch(), cancellationToken);
                }
            }

            if (!reader.Halted && reader.BatchCount > 0)
            {
                await sink(reader.TakeBatch(), cancellationToken);
            }

            return reader.Summary(bytesRead);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The state of one read: header, counters, the batch being filled and the minutes seen. Synchronous, so it can work on spans.</summary>
    private sealed class LineReader(CsvReadLimits limits)
    {
        private readonly Bar[] _batch = new Bar[BatchSize];
        private readonly CsvSessionProbe _probe = new();
        private readonly HashSet<long> _seen = new(200_000);
        private readonly Dictionary<string, long> _rejections = [];
        private readonly List<string> _examples = [];
        private int[]? _roles;
        private int _fieldCount;
        private long _rows;
        private long _accepted;
        private long _repaired;
        private long _rejected;
        private long _milliseconds;
        private long _earliest = long.MaxValue;
        private long _latest = long.MinValue;
        private string? _fileError;

        public int BatchCount { get; private set; }

        public bool BatchFull => BatchCount == BatchSize;

        public bool Halted => _fileError is not null;

        public long LineNumber { get; private set; }

        public void Halt(string error) => _fileError ??= error;

        public ReadOnlyMemory<Bar> TakeBatch()
        {
            var batch = _batch.AsMemory(0, BatchCount);
            BatchCount = 0;
            return batch;
        }

        public CsvReadSummary Summary(long bytesRead) => new(
            _rows,
            _accepted,
            _repaired,
            _rejected,
            _rejections,
            _examples,
            _fileError ?? (_roles is null ? null : BadTimes() ?? _probe.Problem()),
            _accepted > 0 ? _earliest : null,
            _accepted > 0 ? _latest : null,
            bytesRead);

        public void Line(byte[] buffer, int offset, int length)
        {
            LineNumber++;
            var line = buffer.AsSpan(offset, length);
            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (LineNumber == 1 && line.Length >= 3 && line[0] == 0xEF && line[1] == 0xBB && line[2] == 0xBF)
            {
                line = line[3..];
            }

            if (line.IndexOfAnyExcept((byte)' ', (byte)'\t') < 0)
            {
                return;
            }

            if (_roles is null)
            {
                Header(line);
                return;
            }

            _rows++;
            if (_rows > limits.MaxRows)
            {
                Halt(string.Create(CultureInfo.InvariantCulture, $"The file has more than {limits.MaxRows:N0} rows (Data:Csv:MaxRowsPerFile): this is not a normal file of bars."));
                return;
            }

            Row(line);
        }

        private void Header(ReadOnlySpan<byte> line)
        {
            var names = Encoding.UTF8.GetString(line).Split(',');
            var roles = new int[names.Length];
            var found = new bool[Required.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i].Trim().Trim('"').Trim();
                var role = Array.FindIndex(Required, r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase));
                if (role >= 0 && !found[role])
                {
                    found[role] = true;
                    roles[i] = role + 1;
                }
            }

            var missing = Required.Where((_, i) => !found[i]).ToArray();
            if (missing.Length > 0)
            {
                Halt($"The header has no {string.Join(", ", missing.Select(m => $"'{m}'"))} column.");
                return;
            }

            _roles = roles;
            _fieldCount = names.Length;
        }

        private void Row(ReadOnlySpan<byte> line)
        {
            long time = 0, volume = 0;
            double open = 0, high = 0, low = 0, close = 0;
            var field = 0;
            var rest = line;
            while (true)
            {
                var comma = rest.IndexOf((byte)',');
                var value = comma < 0 ? rest : rest[..comma];
                var role = field < _roles!.Length ? _roles[field] : 0;
                if (role != 0)
                {
                    var ok = role switch
                    {
                        1 => long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out time),
                        2 => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out open),
                        3 => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out high),
                        4 => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out low),
                        5 => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out close),
                        _ => TryVolume(value, out volume),
                    };
                    if (!ok)
                    {
                        Reject(Unparseable, "a value is empty or not a number");
                        return;
                    }
                }

                field++;
                if (comma < 0)
                {
                    break;
                }

                rest = rest[(comma + 1)..];
            }

            if (field < _fieldCount)
            {
                Reject(Unparseable, "the row has fewer values than the header");
                return;
            }

            if (time is < MinEpoch or > MaxEpoch)
            {
                if (time is >= MinMillis and <= MaxMillis)
                {
                    _milliseconds++;
                }

                Reject(TimeOutOfRange, "the time is not epoch seconds between 2000 and 2100");
                return;
            }

            if (time > limits.LatestEpochSeconds)
            {
                Reject(TimeInFuture, "the time is more than a day in the future");
                return;
            }

            var verdict = BarRules.Check(new Bar(time, open, high, low, close, volume), out var bar);
            switch (verdict)
            {
                case BarVerdict.NotAWholeMinute:
                    Reject(NotWholeMinute, "the time is not a whole minute");
                    return;
                case BarVerdict.NotANumber:
                    Reject(NotANumber, "a price is not a finite number");
                    return;
                case BarVerdict.NonPositivePrice:
                    Reject(NonPositivePrice, "a price is not above zero");
                    return;
                case BarVerdict.NegativeVolume:
                    Reject(NegativeVolume, "the volume is negative");
                    return;
            }

            if (!_seen.Add(time))
            {
                Reject(Duplicate, "the minute is in the file twice (the first one is kept)");
                return;
            }

            if (verdict == BarVerdict.Repaired)
            {
                _repaired++;
            }

            _accepted++;
            _batch[BatchCount++] = bar;
            _probe.Add(time);
            _earliest = Math.Min(_earliest, time);
            _latest = Math.Max(_latest, time);
            if (_latest - _earliest > limits.MaxSpan.TotalSeconds)
            {
                Halt(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file spans more than {limits.MaxSpan.TotalDays:N0} days (from {Date(_earliest)} on, Data:Csv:MaxSpanDays): this is not a normal file of bars."));
            }
        }

        /// <summary>When most rows have a time that is no epoch-seconds time of today or the past, the file has the wrong time unit or the wrong clock; a few stray rows are only rejected.</summary>
        private string? BadTimes()
        {
            var outOfRange = _rejections.GetValueOrDefault(TimeOutOfRange);
            var future = _rejections.GetValueOrDefault(TimeInFuture);
            if ((outOfRange + future) * 2 <= _rows)
            {
                return null;
            }

            if (_milliseconds * 2 > _rows)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{_milliseconds:N0} of {_rows:N0} times look like epoch milliseconds, but the times must be epoch seconds (nothing was stored)");
            }

            return future > outOfRange
                ? string.Create(CultureInfo.InvariantCulture, $"{future:N0} of {_rows:N0} times are later than one day from now ({Date(limits.LatestEpochSeconds)}): the dates are probably wrong (nothing was stored)")
                : string.Create(CultureInfo.InvariantCulture, $"{outOfRange + future:N0} of {_rows:N0} times are not epoch seconds between 2000 and 2100 (nothing was stored)");
        }

        private static string Date(long epochSeconds) => DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(epochSeconds, MinEpoch, MaxEpoch)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>A whole number; "15773.0" is accepted when it is whole.</summary>
        private static bool TryVolume(ReadOnlySpan<byte> value, out long volume)
        {
            if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out volume))
            {
                return true;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && Math.Abs(number) < 9e15 && Math.Truncate(number) == number)
            {
                volume = (long)number;
                return true;
            }

            volume = 0;
            return false;
        }

        private void Reject(string reason, string text)
        {
            _rejected++;
            _rejections[reason] = _rejections.GetValueOrDefault(reason) + 1;
            if (_examples.Count < CsvPhase.MaxExamples)
            {
                _examples.Add($"line {LineNumber}: {text}");
            }
        }
    }
}
