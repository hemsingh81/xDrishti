using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// The calendar loader (spec P15-04 criteria 1, 3, 8, 10): reads the board-meeting csv files of the inbox that carry their own times, and stores each row once with its own intimation time (one envelope plus one calendar row in one transaction, through
/// <see cref="ICalendarStore"/>). Idempotent: a reloaded file inserts nothing. A bad row never stops the good rows; a bad header refuses the file; a file that cannot be read is counted and left for the next run. No estimate of a date is ever stored. Local files and the filings tables
/// only; no network, no broker.
/// </summary>
internal sealed class LoadCalendarHandler(
    IFilingsSource source,
    IFilingInstruments instruments,
    ICalendarStore store,
    TimeProvider clock,
    ILogger<LoadCalendarHandler> logger) : ICommandHandler<LoadCalendarCommand, RowLoadSummary>
{
    public const string Kind = "calendar";

    public async ValueTask<Result<RowLoadSummary>> HandleAsync(LoadCalendarCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var listing = await source.ListAsync(new FilingQuery(Kind: FilingKind.BoardMeeting), cancellationToken);
        var counts = new RowLoadCounts(Kind);
        foreach (var entry in listing.Accepted.Where(e => e.Broadcast == BroadcastState.RowLevelTs && e.Kind == FilingKind.BoardMeeting))
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
            var rejectedCount = summary.Rejected.Values.Sum();
            var reasons = string.Join(",", summary.Rejected.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}:{r.Value}"));
            RowLoadLog.Rejected(logger, Kind, rejectedCount, reasons);
        }

        return Result.Success(summary);
    }

    private async ValueTask<(int Rows, string? FileReject)> LoadFileAsync(FilingEntry entry, RowLoadCounts counts, CancellationToken cancellationToken)
    {
        var (text, reject) = await RowFileReader.ReadAsync(source, entry, cancellationToken);
        if (text is null)
        {
            counts.Reject(reject!);
            return (0, reject);
        }

        var now = clock.GetUtcNow();
        var parsed = MeetingCsv.Parse(text, now);
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
        var (ordered, ambiguous) = MeetingCsv.Prepare(accepted);
        foreach (var rejected in ambiguous)
        {
            counts.Reject(rejected);
        }

        // Instruments are independent: the rows of one symbol are stored in order (a reschedule after its first row), different symbols in parallel (each store call is one transaction under the lock of its own group).
        var symbols = ordered.GroupBy(r => r.Symbol).Select(g => g.ToList()).ToList();
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
                var canonical = RowCanonical.Meeting(instrumentId, row);
                var undated = row.Intimation.State != BroadcastState.Ok;
                var filing = new NewFiling(
                    instrumentId,
                    FilingKind.BoardMeeting,
                    row.PeriodEnd,
                    row.Time,
                    undated ? BroadcastState.Unavailable : BroadcastState.Ok,
                    undated ? row.Intimation.Reason ?? BroadcastRule.Missing : null,
                    now,
                    RowCanonical.Sha256(canonical),
                    RowCanonical.Bytes(canonical),
                    FileLabel.Of(entry.FileName));
                counts.Insert(await store.InsertAsync(new NewMeeting(filing, row.MeetingDate, row.Purpose, row.Status), token), undated, row.Line);
            }
        });

        return (parsed.Rows.Count + parsed.Rejects.Count, null);
    }
}
