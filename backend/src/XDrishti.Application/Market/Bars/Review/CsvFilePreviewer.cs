using System.IO.Compression;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Review;

/// <summary>What one preview request may still spend: rows and bytes read from the files, stored bars looked up. Shared by the files of the request.</summary>
internal sealed class PreviewBudget(long rows, long bytes, long compare, int files)
{
    public long RowsLeft { get; private set; } = rows;

    public long BytesLeft { get; private set; } = bytes;

    public long CompareLeft { get; private set; } = compare;

    public long RowsRead { get; private set; }

    public long ComparedRows { get; private set; }

    /// <summary>The share of the stored-bar lookups one file gets: the request's total divided by its files, at least one batch.</summary>
    public long CompareShare { get; } = Math.Max(CsvBarReader.BatchSize, compare / Math.Max(1, files));

    public bool Exhausted => RowsLeft <= 0 || BytesLeft <= 0;

    public void Read(long fileRows, long fileBytes)
    {
        RowsLeft -= fileRows;
        BytesLeft -= fileBytes;
        RowsRead += fileRows;
    }

    public void Compared(long count)
    {
        CompareLeft -= count;
        ComparedRows += count;
    }
}

/// <summary>
/// The dry run of one file (ADR 0017 §6): the real <see cref="CsvBarReader"/> with the real row rules, limits and session probe, so what the preview says is what a load would do — only nothing is merged.
/// The batches the reader hands over are counted (trading days, first rows) and, for a file that maps to an instrument, a spread sample of them (all of them up to the request's share) is classified against the stored
/// bars by primary key lookups. The sample is a systematic one: when the retained batches exceed the share, every second one is dropped and the stride doubles, so memory stays within the share and the sample stays spread over the file.
/// </summary>
internal sealed class CsvFilePreviewer(ICsvFolder folder, IBarComparison comparison, IDataSettings settings, TimeProvider clock)
{
    private const int FirstRowCount = 5;

    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public async ValueTask<CsvFilePreview> PreviewAsync(CsvFileEntry entry, PreviewBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(budget);
        if (entry.Mapping == CsvFileMappings.InvalidName)
        {
            return Failed(entry, CsvPreviewVerdicts.Error, "The name is not <SYMBOL>_1m.csv or <SYMBOL>_1m.csv.gz, so the file is never loaded.");
        }

        if (budget.Exhausted)
        {
            return Failed(entry, CsvPreviewVerdicts.Error, "The limit of one preview request was reached before this file; preview fewer files at a time.");
        }

        var compare = entry.Mapping == CsvFileMappings.Mapped;
        var limits = CsvReadLimits.From(settings, clock.GetUtcNow());
        var days = new HashSet<long>();
        var first = new List<Bar>(FirstRowCount);
        var sample = new BatchSample(Math.Max(1, (int)(budget.CompareShare / CsvBarReader.BatchSize)));
        try
        {
            await using var raw = folder.Open(entry.File.Name);
            Stream data = CsvFileName.IsGzip(entry.File.Name) ? new GZipStream(raw, CompressionMode.Decompress) : raw;
            await using (data)
            {
                var summary = await CsvBarReader.ReadAsync(
                    data,
                    (batch, _) =>
                    {
                        foreach (var bar in batch.Span)
                        {
                            days.Add(IndiaDay(bar.EpochSeconds));
                            if (first.Count < FirstRowCount)
                            {
                                first.Add(bar);
                            }
                        }

                        if (compare)
                        {
                            sample.Offer(batch);
                        }

                        return ValueTask.CompletedTask;
                    },
                    cancellationToken,
                    limits);
                budget.Read(summary.Rows, summary.BytesRead);
                return await ResultAsync(entry, summary, days.Count, first, compare && summary.FileError is null ? sample : null, budget, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Failed(entry, CsvPreviewVerdicts.Error, ex is InvalidDataException ? "The file is not a valid gzip file." : "The file could not be read.");
        }
    }

    private async ValueTask<CsvFilePreview> ResultAsync(
        CsvFileEntry entry, CsvReadSummary summary, int tradingDays, List<Bar> first, BatchSample? sample, PreviewBudget budget, CancellationToken cancellationToken)
    {
        CsvPreviewComparison? compared = null;
        if (sample is not null && entry.Instrument is { } instrument)
        {
            long insert = 0, update = 0, keep = 0, same = 0, rows = 0;
            foreach (var batch in sample.Batches)
            {
                var result = await comparison.CompareAsync(instrument.Id, batch, cancellationToken);
                insert += result.Inserted;
                update += result.Updated;
                keep += result.KeptDhan;
                same += result.Unchanged;
                rows += batch.Length;
            }

            budget.Compared(rows);
            compared = new CsvPreviewComparison(insert, update, keep, same, rows, rows < summary.Accepted);
        }

        var refused = summary.FileError is not null;
        return new CsvFilePreview(
            entry.File.Name,
            refused ? CsvPreviewVerdicts.Refused : CsvPreviewVerdicts.Pass,
            summary.FileError,
            entry.State,
            entry.Symbol,
            entry.Mapping,
            entry.Instrument,
            !refused && entry.Mapping == CsvFileMappings.Mapped,
            summary.Rows,
            summary.Accepted,
            summary.Repaired,
            summary.Rejected,
            summary.FirstEpoch is { } firstEpoch ? InIndia(firstEpoch) : null,
            summary.LastEpoch is { } lastEpoch ? InIndia(lastEpoch) : null,
            tradingDays,
            summary.Rejections,
            summary.Examples,
            [.. first.Select(b => new CsvPreviewBar(InIndia(b.EpochSeconds), b.Open, b.High, b.Low, b.Close, b.Volume))],
            compared);
    }

    private static CsvFilePreview Failed(CsvFileEntry entry, string verdict, string message) => new(
        entry.File.Name, verdict, message, entry.State, entry.Symbol, entry.Mapping, entry.Instrument, false, 0, 0, 0, 0, null, null, 0, new Dictionary<string, long>(), [], [], null);

    /// <summary>A file the request ran out of time for (<see cref="PreviewCsvFilesHandler.Deadline"/>), before it or while it was read: nothing is known about its content.</summary>
    public static CsvFilePreview OutOfTime(CsvFileEntry entry) =>
        Failed(entry, CsvPreviewVerdicts.Error, $"The time limit of one preview request ({(int)PreviewCsvFilesHandler.Deadline.TotalSeconds} seconds) was reached before this file was read; preview fewer or smaller files at a time.");

    /// <summary>A file the folder listing does not have: nothing is known about it but the name it was asked for.</summary>
    public static CsvFilePreview NotInFolder(string requested, bool folderExists) => new(
        CsvNames.Echo(requested),
        CsvPreviewVerdicts.Error,
        folderExists ? $"'{CsvNames.Echo(requested)}' is not a file of the CSV folder." : "The CSV folder was not found.",
        null, null, null, null, false, 0, 0, 0, 0, null, null, 0, new Dictionary<string, long>(), [], [], null);

    private static long IndiaDay(long epochSeconds) => Math.DivRem(epochSeconds + (long)India.TotalSeconds, 86_400, out _);

    private static DateTimeOffset InIndia(long epochSeconds) => DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToOffset(India);

    /// <summary>At most <c>capacity</c> whole batches, spread over the file: every batch while they fit, then every second, fourth … one.</summary>
    private sealed class BatchSample(int capacity)
    {
        private readonly List<(long Index, Bar[] Bars)> _kept = [];
        private long _index;
        private long _stride = 1;

        public IEnumerable<Bar[]> Batches => _kept.Select(k => k.Bars);

        public void Offer(ReadOnlyMemory<Bar> batch)
        {
            var index = _index++;
            if (index % _stride != 0)
            {
                return;
            }

            _kept.Add((index, batch.ToArray())); // the reader reuses its array: keep a copy
            while (_kept.Count > capacity)
            {
                _stride *= 2;
                _kept.RemoveAll(k => k.Index % _stride != 0);
            }
        }
    }
}
