using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Depth;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// The announcement loader (spec P15-07 criteria 1 to 3): reads the announcement csv files of the inbox that carry their own times and stores each row once: its text in the files store under its SHA-256 (the envelope's content hash, so the same row in two files is one row),
/// then one envelope plus one <c>filings.announcements</c> row in one transaction through <see cref="IAnnouncementStore"/>. Every row is its own supersede group (no period). A row with no usable time is stored <c>unavailable</c> and counted, never dated. Idempotent: a
/// reloaded file inserts nothing. A bad row never stops the good rows; a bad header refuses the file. The log carries counts and codes only, never a category, subject or text. Local files and the filings tables only; no network, no broker.
/// </summary>
internal sealed class LoadAnnouncementsHandler(
    IFilingsSource source,
    IFilingInstruments instruments,
    IAnnouncementStore store,
    IFilingFiles files,
    IFilingSettings settings,
    TimeProvider clock,
    ILogger<LoadAnnouncementsHandler> logger) : ICommandHandler<LoadAnnouncementsCommand, RowLoadSummary>
{
    public const string Kind = "announcement";

    public async ValueTask<Result<RowLoadSummary>> HandleAsync(LoadAnnouncementsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var listing = await source.ListAsync(new FilingQuery(Kind: FilingKind.Announcement), cancellationToken);
        var counts = new RowLoadCounts(Kind);
        foreach (var entry in listing.Accepted.Where(e => e.Broadcast == BroadcastState.RowLevelTs && e.Kind == FilingKind.Announcement))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Files++;
            var before = counts.Mark();
            var rows = 0;
            string? fileReject = null;
            try
            {
                (rows, fileReject) = await LoadFileAsync(entry, counts, cancellationToken);
            }
#pragma warning disable CA1031 // one file that cannot be loaded must not block the others: it is logged by type, counted and left for the next run
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                counts.Failures++;
                RowLoadLog.Failed(logger, Kind, ex.GetType().Name);
            }

            counts.EndFile(entry.FileName, fileReject, rows, before);
        }

        var summary = counts.ToSummary();
        RowLoadLog.Loaded(logger, Kind, summary.Files, summary.Loaded, summary.Revisions, summary.OutOfOrder, summary.Duplicate, summary.Unavailable, 0);
        if (summary.Rejected.Count > 0)
        {
            var reasons = string.Join(",", summary.Rejected.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}:{r.Value}"));
            var rejectedCount = summary.Rejected.Values.Sum();
            RowLoadLog.Rejected(logger, Kind, rejectedCount, reasons);
        }

        return Result.Success(summary);
    }

    private async ValueTask<(int Rows, string? FileReject)> LoadFileAsync(FilingEntry entry, RowLoadCounts counts, CancellationToken cancellationToken)
    {
        var verdict = DepthDiskGuard.Check(await files.UsedBytesAsync(cancellationToken), files.FreeBytes(), settings.MaxFilesBytes, settings.MinFreeBytes);
        if (verdict is DepthDiskVerdict.OverBudget or DepthDiskVerdict.LowFree)
        {
            var stop = verdict == DepthDiskVerdict.OverBudget ? IngestFilingsHandler.DiskBudget : IngestFilingsHandler.DiskFree;
            counts.Reject(stop);
            return (0, stop);
        }

        var (text, reject) = await RowFileReader.ReadAsync(source, entry, cancellationToken);
        if (text is null)
        {
            counts.Reject(reject!);
            return (0, reject);
        }

        var now = clock.GetUtcNow();
        var parsed = AnnouncementCsv.Parse(text, now);
        if (parsed.FileReject is not null)
        {
            counts.Reject(parsed.FileReject);
            return (0, parsed.FileReject);
        }

        foreach (var rejected in parsed.Rejects)
        {
            counts.Reject(rejected);
        }

        var accepted = parsed.Rows.Where(r => entry.Symbol == FilingSidecar.WildcardSymbol || r.Symbol == entry.Symbol).ToList();
        foreach (var other in parsed.Rows.Except(accepted))
        {
            counts.Reject(new RowReject(other.Line, CalendarRejectReasons.BadRow, "symbol"));
        }

        // Instruments are independent: different symbols are stored in parallel; every row is its own transaction.
        var symbols = accepted.GroupBy(r => r.Symbol).Select(g => g.ToList()).ToList();
        await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = RowLoadCounts.Parallelism, CancellationToken = cancellationToken }, async (rowsOfSymbol, token) =>
        {
            var match = await instruments.ResolveAsync(rowsOfSymbol[0].Symbol, token);
            if (match.Id is not { } instrumentId)
            {
                foreach (var row in rowsOfSymbol)
                {
                    counts.Reject(new RowReject(row.Line, match.Ambiguous ? CalendarRejectReasons.AmbiguousSymbol : CalendarRejectReasons.UnknownInstrument, "symbol"));
                }

                return;
            }

            foreach (var row in rowsOfSymbol)
            {
                await StoreAsync(instrumentId, row, entry, now, counts, token);
            }
        });

        return (parsed.Rows.Count + parsed.Rejects.Count, null);
    }

    private async ValueTask StoreAsync(Guid instrumentId, AnnouncementRow row, FilingEntry entry, DateTimeOffset now, RowLoadCounts counts, CancellationToken cancellationToken)
    {
        var canonical = RowCanonical.Announcement(instrumentId, row);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var sha = RowCanonical.Sha256(canonical);
        using var content = new MemoryStream(bytes);
        var copied = await files.CopyRebuildableAsync(content, sha, bytes.Length, cancellationToken);
        if (copied.IsFailure)
        {
            counts.Reject(new RowReject(row.Line, copied.Error.Code));
            return;
        }

        var undated = row.Broadcast.State != BroadcastState.Ok;
        var filing = new NewFiling(
            instrumentId,
            FilingKind.Announcement,
            null,
            row.Time,
            undated ? BroadcastState.Unavailable : BroadcastState.Ok,
            undated ? row.Broadcast.Reason ?? BroadcastRule.Missing : null,
            now,
            sha,
            bytes.Length,
            FileLabel.Of(entry.FileName));
        counts.Insert(await store.InsertAsync(new NewAnnouncement(filing, row.Category, row.Subject), cancellationToken), undated, row.Line);
    }
}
