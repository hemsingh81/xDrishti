using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Market.Context;

/// <summary>Loads the NSE daily CSVs of NIFTY 50 and INDIA VIX the owner dropped into the context inbox (P3-01d fallback D, ADR 0290). Idempotent, insert-only; local files only.</summary>
public sealed record LoadContextInboxCommand : ICommand;

/// <summary>What a load did: files seen, files moved to failed/, index days and VIX closes written, rows whose day was stored already, and refused rows by closed code.</summary>
public sealed record ContextInboxLoaded(int Files, int FilesFailed, int IndexWritten, int VixWritten, int AlreadyStored, IReadOnlyDictionary<string, int> Rejected);

internal sealed class LoadContextInboxHandler(
    IContextInbox inbox,
    IContextDayStore store,
    ITradingCalendar tradingCalendar,
    TimeProvider clock,
    ILogger<LoadContextInboxHandler> logger) : ICommandHandler<LoadContextInboxCommand, ContextInboxLoaded>
{
    /// <summary>The <c>source</c> label of the days written here.</summary>
    public const string Source = "nse_csv";

    public async ValueTask<Result<ContextInboxLoaded>> HandleAsync(LoadContextInboxCommand command, CancellationToken cancellationToken)
    {
        var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!inbox.Enabled)
        {
            return Result.Success(new ContextInboxLoaded(0, 0, 0, 0, 0, rejected));
        }

        var names = await inbox.ListAsync(cancellationToken);
        if (names.Count == 0)
        {
            return Result.Success(new ContextInboxLoaded(0, 0, 0, 0, 0, rejected));
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(clock.GetUtcNow());
        var index = new Dictionary<DateOnly, ContextCsvRow>();
        var vix = new Dictionary<DateOnly, ContextCsvRow>();
        var verdicts = new List<(string Name, bool Ok)>();
        foreach (var name in names)
        {
            var text = await inbox.ReadAsync(name, cancellationToken);
            if (text is null)
            {
                Count(rejected, ContextCsvCodes.Unreadable);
                verdicts.Add((name, false));
                continue;
            }

            var parsed = ContextCsv.Parse(text, name);
            if (parsed.FileReject is { } fileReject)
            {
                Count(rejected, fileReject);
                verdicts.Add((name, false));
                continue;
            }

            foreach (var reject in parsed.Rejects)
            {
                Count(rejected, reject.Code);
            }

            var target = parsed.Kind == ContextCsvKind.Vix ? vix : index;
            var accepted = 0;
            foreach (var row in parsed.Rows)
            {
                if (!calendar.IsTradingDay(row.Date))
                {
                    Count(rejected, ContextCsvCodes.NotASession);
                }
                else if (row.Date > lastClosed)
                {
                    Count(rejected, ContextCsvCodes.NotFinal);
                }
                else if (target.TryAdd(row.Date, row))
                {
                    accepted++;
                }
            }

            // A file with rows but none usable (all refused) is a failed file; one with some good rows is done.
            verdicts.Add((name, accepted > 0 || parsed.Rows.Count > 0));
        }

        var stored = (await store.ListDatesAsync(cancellationToken)).ToHashSet();
        var fresh = index.Values.Where(r => !stored.Contains(r.Date)).OrderBy(r => r.Date)
            .Select(r => new ContextDayRecord(r.Date, r.High!.Value, r.Low!.Value, r.Close, null, 1, null)).ToList();
        var indexWritten = fresh.Count == 0 ? 0 : await store.InsertAsync(Guid.Empty, fresh, Source, cancellationToken);
        var vixDays = vix.Values.OrderBy(r => r.Date).Select(r => new ContextVixDay(r.Date, r.Close)).ToList();
        var vixWritten = vixDays.Count == 0 ? 0 : await store.InsertVixAsync(vixDays, cancellationToken);
        var already = (index.Count - indexWritten) + (vix.Count - vixWritten);

        foreach (var (name, ok) in verdicts)
        {
            await inbox.MoveAsync(name, ok, cancellationToken);
        }

        var failed = verdicts.Count(v => !v.Ok);
        var reasons = string.Join(",", rejected.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}:{r.Value}"));
        ContextLog.InboxLoaded(logger, names.Count, failed, indexWritten, vixWritten, already, reasons);
        return Result.Success(new ContextInboxLoaded(names.Count, failed, indexWritten, vixWritten, already, rejected));
    }

    private static void Count(Dictionary<string, int> counts, string code) => counts[code] = counts.GetValueOrDefault(code) + 1;
}
