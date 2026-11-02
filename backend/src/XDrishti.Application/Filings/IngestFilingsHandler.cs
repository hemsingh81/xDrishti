using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Depth;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// The filing ingest (spec P15-02 criteria 3 to 5, 7, 9, 10; ADR 0180): list the inbox (P15-01), resolve each symbol by exact match, check the hash, copy the file into the store folder and re-hash the copy, then insert the envelope (one transaction per file, the group rule
/// applied under a lock by the store). Idempotent: a re-run of a whole inbox inserts nothing and copies nothing. The inbox is never changed; no file or row is ever deleted. A file with no usable broadcast time is stored as unavailable and counted, never dated. A <c>.csv</c> whose
/// rows carry their own times is left for the P15-04 loader. Reads local files and writes its own tables; no network, no broker.
/// </summary>
internal sealed class IngestFilingsHandler(
    IFilingsSource source,
    IFilingInstruments instruments,
    IFilingStore store,
    IFilingFiles files,
    IFilingSettings settings,
    TimeProvider clock,
    ILogger<IngestFilingsHandler> logger) : ICommandHandler<IngestFilingsCommand, FilingIngestSummary>
{
    public const string DiskBudget = "disk_budget";

    public const string DiskFree = "disk_free";

    public async ValueTask<Result<FilingIngestSummary>> HandleAsync(IngestFilingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var listing = await source.ListAsync(FilingQuery.All, cancellationToken);
        var run = new Counts();
        foreach (var rejected in listing.Rejected)
        {
            run.Reject(rejected.Reason);
        }

        var used = await files.UsedBytesAsync(cancellationToken);
        var warned = false;
        foreach (var entry in listing.Accepted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Broadcast == BroadcastState.RowLevelTs)
            {
                run.Deferred++;
                continue;
            }

            var verdict = DepthDiskGuard.Check(used, files.FreeBytes(), settings.MaxFilesBytes, settings.MinFreeBytes);
            if (verdict is DepthDiskVerdict.OverBudget or DepthDiskVerdict.LowFree)
            {
                run.Stopped = verdict == DepthDiskVerdict.OverBudget ? DiskBudget : DiskFree;
                FilingsIngestLog.DiskStop(logger, run.Stopped, used);
                break;
            }

            if (verdict == DepthDiskVerdict.Warning && !warned)
            {
                warned = true;
                FilingsIngestLog.DiskWarning(logger, used, settings.MaxFilesBytes);
            }

            try
            {
                used += await IngestOneAsync(entry, run, cancellationToken);
            }
#pragma warning disable CA1031 // one file that cannot be stored must not block the others: it is logged by type, counted and left for the next run
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                run.Failures++;
                FilingsIngestLog.Failed(logger, ex.GetType().Name);
            }
        }

        var summary = run.ToSummary(listing);
        FilingsIngestLog.Ingested(logger, summary.Stored, summary.Revisions, summary.OutOfOrder, command.Manual ? "manual" : "daily");
        if (summary.Duplicate > 0)
        {
            FilingsIngestLog.Duplicate(logger, summary.Duplicate);
        }

        if (summary.Unavailable > 0)
        {
            var reasons = Reasons(run.UnavailableReasons);
            FilingsIngestLog.Unavailable(logger, summary.Unavailable, reasons);
        }

        if (run.RejectedReasons.Count > 0)
        {
            var reasons = Reasons(run.RejectedReasons);
            var rejectedCount = summary.Rejected.Values.Sum();
            FilingsIngestLog.Rejected(logger, rejectedCount, reasons);
        }

        FilingsIngestLog.Run(logger, summary.Text);
        return Result.Success(summary);
    }

    // Returns the bytes added to the store folder by this file.
    private async ValueTask<long> IngestOneAsync(FilingEntry entry, Counts run, CancellationToken cancellationToken)
    {
        var match = await instruments.ResolveAsync(entry.Symbol, cancellationToken);
        if (match.Id is not { } instrumentId)
        {
            run.Reject(match.Ambiguous ? "ambiguous_symbol" : "unknown_instrument");
            return 0;
        }

        var filing = new NewFiling(
            instrumentId,
            entry.Kind,
            entry.PeriodEnd,
            entry.Broadcast == BroadcastState.Ok ? NewFiling.ToMicroseconds(entry.BroadcastTs) : null,
            entry.Broadcast,
            entry.Broadcast == BroadcastState.Ok ? null : entry.BroadcastReason ?? "missing",
            clock.GetUtcNow(),
            entry.Sha256,
            entry.SizeBytes,
            entry.FileName);

        var existing = await store.FindByHashAsync(entry.Sha256, cancellationToken);
        if (existing is not null)
        {
            Record(run, filing, new FilingInsertResult(filing.SameSidecar(existing) ? FilingInsertOutcome.Duplicate : FilingInsertOutcome.SidecarConflict, existing));
            return 0;
        }

        var opened = await source.OpenAsync(entry.Ref, cancellationToken);
        if (opened.IsFailure)
        {
            run.Reject(opened.Error.Code);
            return 0;
        }

        Result<bool> copied;
        await using (var stream = opened.Value)
        {
            copied = await CopyAsync(stream, entry, cancellationToken);
        }

        if (copied.IsFailure)
        {
            run.Reject(copied.Error.Code);
            return 0;
        }

        var result = await store.InsertAsync(filing, cancellationToken);
        Record(run, filing, result);
        return copied.Value ? entry.SizeBytes : 0;
    }

    private async ValueTask<Result<bool>> CopyAsync(Stream stream, FilingEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            return await files.CopyAsync(stream, entry.Sha256, entry.SizeBytes, cancellationToken);
        }
        catch (FilingContentChangedException)
        {
            return Result.Failure<bool>(Error.Validation(FilingRejectReasons.ContentChanged, "The file changed while it was read."));
        }
        catch (IOException)
        {
            return Result.Failure<bool>(Error.Validation(FilingRejectReasons.Unreadable, "The file could not be read."));
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure<bool>(Error.Validation(FilingRejectReasons.Unreadable, "The file could not be read."));
        }
    }

    private void Record(Counts run, NewFiling filing, FilingInsertResult result)
    {
        switch (result.Outcome)
        {
            case FilingInsertOutcome.Stored:
            case FilingInsertOutcome.StoredOutOfOrder:
                run.Stored++;
                run.Revisions += result.Supersedes is null ? 0 : 1;
                run.OutOfOrder += result.Outcome == FilingInsertOutcome.StoredOutOfOrder ? 1 : 0;
                if (filing.State != BroadcastState.Ok)
                {
                    run.Unavailable++;
                    run.UnavailableReasons[filing.UnavailableReason!] = run.UnavailableReasons.GetValueOrDefault(filing.UnavailableReason!) + 1;
                }

                break;
            case FilingInsertOutcome.Duplicate:
                run.Duplicate++;
                break;
            case FilingInsertOutcome.SidecarConflict:
                run.SidecarConflict++;
                var stored = result.Existing!;
                FilingsIngestLog.SidecarConflict(
                    logger,
                    stored.Id,
                    stored.InstrumentId,
                    FilingKinds.Code(stored.Kind),
                    Date(stored.PeriodEnd),
                    Time(stored.BroadcastTs),
                    filing.InstrumentId,
                    FilingKinds.Code(filing.Kind),
                    Date(filing.PeriodEnd),
                    Time(filing.BroadcastTs));
                break;
            case FilingInsertOutcome.AmbiguousOrder:
                run.Reject("ambiguous_order");
                break;
            case FilingInsertOutcome.BadSupersede:
                run.Reject("bad_supersede");
                break;
            default:
                throw new InvalidOperationException("Unknown insert outcome.");
        }
    }

    private static string Date(DateOnly? date) => date is { } d ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "none";

    private static string Time(DateTimeOffset? time) => time is { } t ? t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture) : "none";

    private static string Reasons(Dictionary<string, int> reasons) => string.Join(",", reasons.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}:{r.Value}"));

    private sealed class Counts
    {
        public int Stored { get; set; }

        public int Revisions { get; set; }

        public int OutOfOrder { get; set; }

        public int Duplicate { get; set; }

        public int SidecarConflict { get; set; }

        public int Unavailable { get; set; }

        public int Deferred { get; set; }

        public int Failures { get; set; }

        public string? Stopped { get; set; }

        public Dictionary<string, int> RejectedReasons { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> UnavailableReasons { get; } = new(StringComparer.Ordinal);

        public void Reject(string reason) => RejectedReasons[reason] = RejectedReasons.GetValueOrDefault(reason) + 1;

        public FilingIngestSummary ToSummary(FilingListing listing) =>
            new(listing.Accepted.Count + listing.Rejected.Count, Stored, Revisions, OutOfOrder, Duplicate, SidecarConflict, Unavailable, Deferred, RejectedReasons, listing.Problems, Failures, Stopped);
    }
}
