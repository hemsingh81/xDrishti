using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>The status of one simulation (SR-04). Read only.</summary>
internal sealed class GetSimDaysHandler(ISimDaysStore store, ITradingCalendar calendars, SimDaysHoldOut holdOut, SimDaysOptions options) : IQueryHandler<GetSimDaysQuery, Result<SimDaysView>>
{
    public async ValueTask<Result<SimDaysView>> HandleAsync(GetSimDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await store.GetAsync(query.Id, cancellationToken) is not { } row)
        {
            return Result.Failure<SimDaysView>(SimDays.NotFound);
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var hasNew = false;
        if (row.Phase == SimDays.Complete && await holdOut.HistoryAsync(cancellationToken) is { } history && history.LastComplete > row.End)
        {
            // reads only: a free look of block 1 is claimed by Continue, never by the status
            var answer = await holdOut.ResolveAsync(history.LastComplete, cancellationToken);
            hasNew = SimDaysSupport.HasNewSessions(row, history.LastComplete, answer.End, calendar);
        }

        return Result.Success(SimDaysSupport.View(row, calendar, options, hasNew));
    }
}

/// <summary>One stored day, or the settled cursor day before it is advanced (SR-04, AC 7). Read only.</summary>
internal sealed class GetSimDayHandler(ISimDaysStore store) : IQueryHandler<GetSimDayQuery, Result<SimDaySnapshotView>>
{
    public async ValueTask<Result<SimDaySnapshotView>> HandleAsync(GetSimDayQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await store.GetAsync(query.Id, cancellationToken) is not { } row)
        {
            return Result.Failure<SimDaySnapshotView>(SimDays.NotFound);
        }

        if (await store.SnapshotAsync(row.Id, query.Session, cancellationToken) is { } day)
        {
            return Result.Success(SimDaysSupport.Snapshot(row, day.Session, true, day.RecordJson, day.TradesJson, day.ResultHash));
        }

        return row.Pending is { } pending && pending.Session == query.Session
            ? Result.Success(SimDaysSupport.Snapshot(row, pending.Session, false, pending.RecordJson, pending.TradesJson, pending.ResultHash))
            : Result.Failure<SimDaySnapshotView>(Error.NotFound("sim.day_not_found", "That day is not settled (yet)."));
    }
}
