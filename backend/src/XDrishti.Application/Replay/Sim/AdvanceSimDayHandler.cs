using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>Stores the settled cursor day as an immutable snapshot and moves the cursor to the next trading session, or marks the run complete at its end (SR-04, AC 7). Before the day is settled: 409 <c>sim.not_settled</c>.</summary>
internal sealed class AdvanceSimDayHandler(ISimDaysStore store, ITradingCalendar calendars, SimDaysOptions options, TimeProvider time, ILogger<AdvanceSimDayHandler> logger) : ICommandHandler<AdvanceSimDayCommand, SimDaysView>
{
    public async ValueTask<Result<SimDaysView>> HandleAsync(AdvanceSimDayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await store.GetAsync(command.Id, cancellationToken) is not { } row)
        {
            return Result.Failure<SimDaysView>(SimDays.NotFound);
        }

        if (row.Phase != SimDays.Settled || row.Pending is not { } pending || pending.Session != row.Cursor)
        {
            return Result.Failure<SimDaysView>(Error.Conflict(SimDays.NotSettledCode, "The day is not settled; settle it first."));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var r = ReplayJson.Read<Domain.Replay.ReplaySessionRecord>(pending.RecordJson);
        var snapshot = new SimDaySnapshotRow(pending.Session, pending.RecordJson, ReplayJson.Canonical(r?.Taken ?? []), ReplayJson.Canonical(r?.Blocked ?? []), pending.TradesJson, pending.EquityClose, pending.ResultHash, pending.EndInForce);
        var next = SimDaysSupport.Next(calendar, row.Cursor, row.End);
        if (!await store.AdvanceAsync(row.Id, snapshot, next, time.GetUtcNow(), cancellationToken))
        {
            return Result.Failure<SimDaysView>(Error.Conflict(SimDays.NotSettledCode, "The day was advanced or the simulation moved meanwhile."));
        }

        SimDaysLog.Advanced(logger, row.Id, row.Cursor, next is null);
        return Result.Success(SimDaysSupport.View(await store.GetAsync(row.Id, cancellationToken) ?? row, calendar, options));
    }
}
