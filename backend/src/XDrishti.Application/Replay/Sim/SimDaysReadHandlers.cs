using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>The status of one simulation (SR-04). Read only.</summary>
internal sealed class GetSimDaysHandler(ISimDaysStore store, ITradingCalendar calendars, SimDaysOptions options) : IQueryHandler<GetSimDaysQuery, Result<SimDaysView>>
{
    public async ValueTask<Result<SimDaysView>> HandleAsync(GetSimDaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store.GetAsync(query.Id, cancellationToken) is { } row
            ? Result.Success(SimDaysSupport.View(row, await calendars.GetAsync(cancellationToken), options))
            : Result.Failure<SimDaysView>(SimDays.NotFound);
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
