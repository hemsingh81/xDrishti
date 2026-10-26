using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Market.Context;

/// <summary>Computes the breadth of the context days that have none (P3-01e): the owner's tracked shares above their average, from stored daily bars up to each day. Written once; idempotent. A day that can still gain bars is left for a later run.</summary>
public sealed record ComputeBreadthCommand : ICommand;

/// <summary>What a run did: days set, of which insufficient, days left for later (the last closed session can still gain stock bars), and the missing prerequisite (null when none).</summary>
public sealed record BreadthComputed(int Computed, int Insufficient, int Pending, string? Missing);

internal sealed class ComputeBreadthHandler(
    IContextSettings settings,
    IBreadthSource source,
    IContextDayStore store,
    ITradingCalendar tradingCalendar,
    TimeProvider clock,
    ILogger<ComputeBreadthHandler> logger) : ICommandHandler<ComputeBreadthCommand, BreadthComputed>
{
    /// <summary>The code of "no tracked share exists": nothing is written (a number for no shares would be a default).</summary>
    public const string NoShares = "no_shares";

    public async ValueTask<Result<BreadthComputed>> HandleAsync(ComputeBreadthCommand command, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return Result.Success(new BreadthComputed(0, 0, 0, ContextPrerequisites.Disabled));
        }

        var started = clock.GetTimestamp();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(clock.GetUtcNow());
        var stored = await store.ListBreadthAsync(cancellationToken);
        var todo = stored
            .Where(r => r.State is null || (r.State == BreadthResult.Insufficient && calendar.TradingDaysBetween(r.Date, lastClosed) < settings.BreadthGraceSessions))
            .ToList();
        if (todo.Count == 0)
        {
            return Result.Success(new BreadthComputed(0, 0, 0, null));
        }

        var sma = settings.BreadthSmaSessions;
        var sessions = BreadthSessions.Between(calendar, todo[0].Date, todo[^1].Date, sma);
        var data = await source.ReadAsync(BreadthSessions.WindowStart(sessions, todo[0].Date, sma), cancellationToken);
        if (data.Shares.Count == 0)
        {
            return Result.Success(new BreadthComputed(0, 0, 0, NoShares));
        }

        var options = new BreadthOptions(sma, settings.BreadthMinVoters, settings.BreadthMinShare, settings.BreadthMinBarShare);
        var writes = new List<BreadthWrite>();
        var pending = 0;
        var insufficient = 0;
        foreach (var row in todo)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = BreadthCalculator.Compute(row.Date, sessions, data.Shares, data.ExDates, options);

            // A day that can still gain bars is not written: the last closed session waits until every share has its bar or the day is older.
            if (row.Date >= lastClosed && !data.Shares.All(s => s.Closes.ContainsKey(row.Date)))
            {
                pending++;
                continue;
            }

            if (row.State is not null && Same(row, result))
            {
                continue;
            }

            writes.Add(new BreadthWrite(row.Date, result, row.State is not null));
            if (result.State == BreadthResult.Insufficient)
            {
                insufficient++;
            }
        }

        var written = writes.Count == 0 ? 0 : await store.SetBreadthAsync(writes, cancellationToken);
        var ms = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        ContextLog.BreadthComputed(logger, written, insufficient, pending, ms);
        return Result.Success(new BreadthComputed(written, insufficient, pending, null));
    }

    internal static bool Same(StoredBreadth stored, BreadthResult result) =>
        stored.State == result.State && stored.Voters == result.Voters && stored.Universe == result.Universe && stored.Excluded == result.Excluded && stored.Value == result.Value;
}
