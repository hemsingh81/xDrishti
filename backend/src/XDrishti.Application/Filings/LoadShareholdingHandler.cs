using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// The shareholding loader (spec P15-04 criteria 7, 8, 10): reads the shareholding csv files of the inbox that carry their own times and stores each (symbol, period) group once, with its time, as one envelope plus its category lines in one transaction through
/// <see cref="IShareholdingStore"/>. A blank percentage is stored unavailable, never zero; a group whose four categories are off 100 by more than 1.0 point is stored and counted <c>sum_off</c>, never corrected. A later group of the same key supersedes the earlier one. Local
/// files and the filings tables only; no network, no broker.
/// </summary>
internal sealed class LoadShareholdingHandler(
    IFilingsSource source,
    IFilingInstruments instruments,
    IShareholdingStore store,
    TimeProvider clock,
    ILogger<LoadShareholdingHandler> logger) : ICommandHandler<LoadShareholdingCommand, RowLoadSummary>
{
    public const string Kind = "shareholding";

    public async ValueTask<Result<RowLoadSummary>> HandleAsync(LoadShareholdingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var listing = await source.ListAsync(new FilingQuery(Kind: FilingKind.Shareholding), cancellationToken);
        var counts = new RowLoadCounts(Kind);
        foreach (var entry in listing.Accepted.Where(e => e.Broadcast == BroadcastState.RowLevelTs && e.Kind == FilingKind.Shareholding))
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
        RowLoadLog.Loaded(logger, Kind, summary.Files, summary.Loaded, summary.Revisions, summary.OutOfOrder, summary.Duplicate, summary.Unavailable, summary.SumOff);
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
        var parsed = ShareholdingCsv.Parse(text, now);
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
        var (groups, groupRejects) = ShareholdingGroups.Build(accepted);
        foreach (var rejected in groupRejects)
        {
            counts.Reject(rejected);
        }

        // Instruments are independent: the groups of one symbol are stored in order (a later quarter after an earlier one), different symbols in parallel (each store call is one transaction under the lock of its own group).
        var symbols = groups.GroupBy(g => g.Symbol).Select(g => g.ToList()).ToList();
        await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = RowLoadCounts.Parallelism, CancellationToken = cancellationToken }, async (groupsOfSymbol, token) =>
        {
            var match = await instruments.ResolveAsync(groupsOfSymbol[0].Symbol, token);
            if (match.Id is not { } instrumentId)
            {
                foreach (var group in groupsOfSymbol)
                {
                    counts.Reject(new RowReject(group.Line, match.Ambiguous ? CalendarRejectReasons.AmbiguousSymbol : CalendarRejectReasons.UnknownInstrument, "symbol"));
                    counts.Reject(match.Ambiguous ? CalendarRejectReasons.AmbiguousSymbol : CalendarRejectReasons.UnknownInstrument, group.Lines.Count - 1);
                }

                return;
            }

            foreach (var group in groupsOfSymbol)
            {
                var canonical = RowCanonical.Shareholding(instrumentId, group);
                var undated = group.Broadcast.State != BroadcastState.Ok;
                var filing = new NewFiling(
                    instrumentId,
                    FilingKind.Shareholding,
                    group.PeriodEnd,
                    group.Time,
                    undated ? BroadcastState.Unavailable : BroadcastState.Ok,
                    undated ? group.Broadcast.Reason ?? BroadcastRule.Missing : null,
                    now,
                    RowCanonical.Sha256(canonical),
                    RowCanonical.Bytes(canonical),
                    FileLabel.Of(entry.FileName));
                var result = await store.InsertAsync(new NewShareholding(filing, group.Lines), token);
                counts.Insert(result, undated, group.Line);
                if (group.SumOff && result.Outcome is FilingInsertOutcome.Stored or FilingInsertOutcome.StoredOutOfOrder)
                {
                    counts.AddSumOff();
                }
            }
        });

        return (parsed.Rows.Count + parsed.Rejects.Count, null);
    }
}
