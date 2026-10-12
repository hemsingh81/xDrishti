using System.IO.Compression;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Pipeline;

/// <summary>
/// The CSV phase (ADR 0016 §4, §5): for each <c>&lt;SYMBOL&gt;_1m.csv[.gz]</c> in the folder that is new or changed (or all, when forced), finds the instrument
/// (<c>NSE:&lt;SYMBOL&gt;</c>, a share or ETF that is not retired; unknown and ambiguous symbols are listed and skipped, never guessed), streams the file through the
/// row rules into the store, merges it in one transaction (CSV never overwrites Dhan) and records the file; at the end the instruments that
/// have bars from CSV files (loaded now or in an earlier run, so a tracking that failed is tried again) are tracked, unless they have ever had a tracking row: an
/// instrument the owner untracked stays untracked. One file failing does not stop the
/// others. A file is read within the limits of <c>Data:Csv</c> and checked for a wrong clock (<see cref="CsvSessionProbe"/>) before anything of it is merged.
/// </summary>
internal sealed class CsvPhaseRunner(
    ICsvFolder folder,
    ICsvFileLog log,
    IListedInstruments listed,
    IBarStore bars,
    IInstrumentCatalog catalog,
    InstrumentTracking tracking,
    IMarketUnitOfWork unitOfWork,
    IDataSettings settings,
    TimeProvider clock,
    ILogger<CsvPhaseRunner> logger)
{
    private const string TrackedVia = "CSV load";

    public async ValueTask<CsvPhase> RunAsync(DataJob job, JobReporter reporter, CancellationToken cancellationToken)
    {
        var phase = CsvPhase.Empty(PhaseStatus.Running) with { StartedAt = clock.GetUtcNow() };
        await reporter.ReportAsync(phase, cancellationToken, publishNow: true);

        if (!folder.Exists())
        {
            return End(phase, PhaseStatus.Skipped, DataErrors.CsvFolderMissingCode, $"The CSV folder {folder.Path} was not found. Mount it into the API (Data:CsvFolder) and run again.");
        }

        if (await catalog.CountActiveAsync(cancellationToken) == 0)
        {
            return End(phase, PhaseStatus.Failed, DataErrors.MasterMissingCode, DataErrors.MasterMissing.Message);
        }

        var files = (await folder.ListAsync(cancellationToken))
            .Where(f => CsvFileName.TryParse(f.Name, out _))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
        phase = phase with { Total = files.Count };
        if (files.Count == 0)
        {
            return End(phase, PhaseStatus.Succeeded, null, $"There are no <SYMBOL>_1m.csv files in {folder.Path}.");
        }

        var loaded = await log.ListAsync(cancellationToken);
        var symbolOf = files.ToDictionary(f => f.Name, f => CsvFileName.Canonical(CsvFileName.TryParse(f.Name, out var s) ? s : f.Name), StringComparer.Ordinal);
        var matches = await listed.FindAsync([.. symbolOf.Values.Distinct(StringComparer.Ordinal)], cancellationToken);
        var filesPerSymbol = symbolOf.Values.GroupBy(s => s, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var rejections = new Dictionary<string, long>(StringComparer.Ordinal);
        var examples = new List<string>();
        var unknown = new List<string>();
        var ambiguous = new List<string>();
        var failed = new List<string>();
        var withBars = new HashSet<Guid>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reporter.Ended)
            {
                break;
            }

            var symbol = symbolOf[file.Name];
            var current = phase;
            if (!job.ForceCsv && loaded.TryGetValue(file.Name, out var previous) && previous.Size == file.Size && previous.ModifiedAt == file.ModifiedAt)
            {
                phase = current with { Done = current.Done + 1, FilesUnchanged = current.FilesUnchanged + 1 };
                if (previous.Rows > 0 && matches.TryGetValue(symbol, out var unchangedIds) && unchangedIds.Count == 1 && filesPerSymbol[symbol] == 1)
                {
                    withBars.Add(unchangedIds[0]); // its bars were stored by an earlier run: a tracking that failed then is tried again now
                }
            }
            else if (!matches.TryGetValue(symbol, out var ids))
            {
                AddCapped(unknown, symbol);
                phase = current with { Done = current.Done + 1, FilesSkipped = current.FilesSkipped + 1 };
            }
            else if (ids.Count != 1 || filesPerSymbol[symbol] != 1)
            {
                AddCapped(ambiguous, symbol);
                phase = current with { Done = current.Done + 1, FilesSkipped = current.FilesSkipped + 1 };
            }
            else
            {
                var outcome = await LoadFileAsync(file, ids[0], cancellationToken);
                if (outcome.Failure is not null)
                {
                    failed.Add($"{file.Name}: {outcome.Failure}");
                    phase = current with { Done = current.Done + 1, FilesFailed = current.FilesFailed + 1 };
                }
                else
                {
                    if (outcome.Summary!.Accepted > 0)
                    {
                        withBars.Add(ids[0]);
                    }

                    foreach (var (reason, count) in outcome.Summary.Rejections)
                    {
                        rejections[reason] = rejections.GetValueOrDefault(reason) + count;
                    }

                    foreach (var example in outcome.Summary.Examples)
                    {
                        if (examples.Count < CsvPhase.MaxExamples)
                        {
                            examples.Add($"{file.Name} {example}");
                        }
                    }

                    var merged = outcome.Merge!;
                    phase = current with
                    {
                        Done = current.Done + 1,
                        FilesLoaded = current.FilesLoaded + 1,
                        Rows = current.Rows + outcome.Summary.Rows,
                        Inserted = current.Inserted + merged.Inserted,
                        Updated = current.Updated + merged.Updated,
                        Unchanged = current.Unchanged + merged.Unchanged,
                        KeptDhan = current.KeptDhan + merged.KeptDhan,
                        Repaired = current.Repaired + outcome.Summary.Repaired,
                        Rejected = current.Rejected + outcome.Summary.Rejected,
                    };
                }
            }

            phase = Lists(phase, rejections, examples, unknown, ambiguous, failed);
            await reporter.ReportAsync(phase, cancellationToken);
        }

        if (reporter.Ended)
        {
            return End(phase, PhaseStatus.Failed, null, "The job was ended elsewhere (marked failed as interrupted); the phase stopped.");
        }

        var tracked = await TrackAsync(job, reporter, withBars, cancellationToken);
        phase = phase with { InstrumentsTracked = tracked };
        return phase.FilesFailed > 0
            ? End(phase, PhaseStatus.Failed, null, $"{phase.FilesFailed} of {phase.Total} files could not be loaded: {failed[0]}")
            : End(phase, PhaseStatus.Succeeded, null, null);
    }

    private async ValueTask<FileOutcome> LoadFileAsync(CsvFileInfo file, Guid instrumentId, CancellationToken cancellationToken)
    {
        try
        {
            await using var session = await bars.BeginMergeAsync(instrumentId, BarSource.Csv, cancellationToken);
            await using var raw = folder.Open(file.Name);
            Stream data = CsvFileName.IsGzip(file.Name) ? new GZipStream(raw, CompressionMode.Decompress) : raw;
            await using (data)
            {
                var summary = await CsvBarReader.ReadAsync(data, (batch, token) => session.AddAsync(batch, token), cancellationToken, CsvReadLimits.From(settings, clock.GetUtcNow()));
                if (summary.FileError is not null)
                {
                    // Leaving without completing the session discards the batches staged so far: nothing of a file with a wrong clock or over a limit is stored.
                    DataLog.CsvFileFailed(logger, file.Name, summary.FileError);
                    return new FileOutcome(summary.FileError, null, null);
                }

                var mark = new CsvFileMark(file.Name, file.Size, file.ModifiedAt, summary.Rows, summary.Repaired, summary.Rejected);
                var merge = await session.CompleteAsync(mark, cancellationToken);
                DataLog.CsvFileLoaded(logger, file.Name, summary.Rows, merge.Inserted, merge.Updated, merge.Unchanged, summary.Repaired, summary.Rejected);
                return new FileOutcome(null, summary, merge);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            DataLog.CsvFileFailed(logger, file.Name, ex.Message);
            return new FileOutcome(ex is InvalidDataException ? "the file is not a valid gzip file" : "the file could not be read", null, null);
        }
#pragma warning disable CA1031 // one file failing (for example the database refusing it) must not stop the others; it is recorded as failed and logged
        catch (Exception ex)
#pragma warning restore CA1031
        {
            DataLog.CsvFileCrashed(logger, file.Name, ex);
            return new FileOutcome("the file could not be stored", null, null);
        }
    }

    /// <summary>
    /// Tracks, once at the end (through the same service as the tracking screen), the instruments that have bars from CSV files and have never had a tracking row:
    /// an instrument the owner untracked is not tracked again behind their back. Returns how many were newly tracked.
    /// </summary>
    private async ValueTask<int> TrackAsync(DataJob job, JobReporter reporter, HashSet<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        if (instrumentIds.Count == 0)
        {
            return 0;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = await tracking.TrackNeverTrackedAsync(instrumentIds, job.RequestedBy, clock.GetUtcNow(), TrackedVia, cancellationToken);
            if (result.IsFailure)
            {
                DataLog.TrackingFailed(logger, job.Id, result.Error.Message);
                await reporter.WarnAsync($"The loaded instruments could not be tracked: {result.Error.Message}", cancellationToken);
                return 0;
            }

            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                return result.Value.Changed;
            }
        }

        DataLog.TrackingFailed(logger, job.Id, "the tracked list kept changing");
        await reporter.WarnAsync("The loaded instruments could not be tracked: the tracked list was changed at the same time. Track them in the Instruments screen.", cancellationToken);
        return 0;
    }

    private static CsvPhase Lists(
        CsvPhase phase,
        Dictionary<string, long> rejections,
        List<string> examples,
        List<string> unknown,
        List<string> ambiguous,
        List<string> failed) => phase with
        {
            Rejections = new Dictionary<string, long>(rejections),
            RejectionExamples = [.. examples],
            UnknownSymbols = [.. unknown],
            AmbiguousSymbols = [.. ambiguous],
            FailedFiles = [.. failed.Take(CsvPhase.MaxExamples)],
        };

    private static void AddCapped(List<string> symbols, string symbol)
    {
        if (symbols.Count < CsvPhase.MaxSymbols)
        {
            symbols.Add(symbol);
        }
    }

    private CsvPhase End(CsvPhase phase, string status, string? code, string? message) =>
        phase with { Status = status, Code = code, Message = message, FinishedAt = clock.GetUtcNow() };

    private sealed record FileOutcome(string? Failure, CsvReadSummary? Summary, BarMergeResult? Merge);
}
