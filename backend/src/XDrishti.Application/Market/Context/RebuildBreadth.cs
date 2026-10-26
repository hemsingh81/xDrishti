using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Market.Context;

/// <summary>
/// Recomputes the breadth of the stored context days from <see cref="From"/> to <see cref="To"/> (P3-01e): the only way a stored breadth changes. A dry run unless <see cref="Write"/>; it lists each changed day with the old and the new figure.
/// </summary>
public sealed record RebuildBreadthCommand(DateOnly From, DateOnly To, bool Write) : ICommand;

/// <summary>One day whose breadth differs from the stored one: the old figure (null when it had none) and the new.</summary>
public sealed record BreadthChange(DateOnly Date, double? OldValue, string? OldState, int? OldVoters, double? NewValue, string NewState, int NewVoters, int NewUniverse);

/// <summary>What a rebuild looked at and changed; <paramref name="Written"/> is false for a dry run. <paramref name="Waiting"/> are days that can still gain bars (the last closed session while a share lacks its bar): never written, as in the daily run.</summary>
public sealed record BreadthRebuilt(int Days, bool Written, IReadOnlyList<BreadthChange> Changes, string? Missing, IReadOnlyList<DateOnly>? Waiting = null);

internal sealed class RebuildBreadthHandler(
    IContextSettings settings,
    IBreadthSource source,
    IContextDayStore store,
    ITradingCalendar tradingCalendar,
    TimeProvider clock,
    ILogger<RebuildBreadthHandler> logger) : ICommandHandler<RebuildBreadthCommand, BreadthRebuilt>
{
    public const int MaxSessions = 400;

    public static readonly Error TooWide = Error.Validation("breadth.range_too_wide", "The range may span at most 400 sessions.");

    public static readonly Error BadRange = Error.Validation("breadth.bad_range", "--from must not be after --to.");

    public async ValueTask<Result<BreadthRebuilt>> HandleAsync(RebuildBreadthCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.From > command.To)
        {
            return Result.Failure<BreadthRebuilt>(BadRange);
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var sma = settings.BreadthSmaSessions;
        var sessions = BreadthSessions.Between(calendar, command.From, command.To, sma);
        if (sessions.Count(d => d >= command.From) > MaxSessions)
        {
            return Result.Failure<BreadthRebuilt>(TooWide);
        }

        var days = (await store.ListBreadthAsync(cancellationToken)).Where(r => r.Date >= command.From && r.Date <= command.To).ToList();
        if (days.Count == 0)
        {
            return Result.Success(new BreadthRebuilt(0, command.Write, [], null));
        }

        var data = await source.ReadAsync(BreadthSessions.WindowStart(sessions, days[0].Date, sma), cancellationToken);
        if (data.Shares.Count == 0)
        {
            return Result.Success(new BreadthRebuilt(days.Count, command.Write, [], ComputeBreadthHandler.NoShares));
        }

        var options = new BreadthOptions(sma, settings.BreadthMinVoters, settings.BreadthMinShare, settings.BreadthMinBarShare);
        var changes = new List<BreadthChange>();
        var writes = new List<BreadthWrite>();
        var waiting = new List<DateOnly>();
        var lastClosed = calendar.LastClosedSession(clock.GetUtcNow());
        foreach (var row in days)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Date >= lastClosed && !data.Shares.All(s => s.Closes.ContainsKey(row.Date)))
            {
                waiting.Add(row.Date); // can still gain bars: not rebuilt, as the daily run leaves it
                continue;
            }

            var result = BreadthCalculator.Compute(row.Date, sessions, data.Shares, data.ExDates, options);
            if (row.State is not null && ComputeBreadthHandler.Same(row, result))
            {
                continue;
            }

            changes.Add(new BreadthChange(row.Date, row.Value, row.State, row.Voters, result.Value, result.State, result.Voters, result.Universe));
            writes.Add(new BreadthWrite(row.Date, result, true));
        }

        if (command.Write && writes.Count > 0)
        {
            await store.ReplaceBreadthAsync(writes, cancellationToken);
        }

        ContextLog.BreadthRebuilt(logger, days.Count, changes.Count, command.Write);
        return Result.Success(new BreadthRebuilt(days.Count, command.Write, changes, null, waiting));
    }
}
