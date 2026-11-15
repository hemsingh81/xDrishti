using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>Turns auto-run on or off (SR-04, AC 8). Off stops after the current day; manual steps stay allowed in between and auto-run resumes from the stored cursor.</summary>
internal sealed class SetSimAutoHandler(ISimDaysStore store, ITradingCalendar calendars, SimDaysOptions options, TimeProvider time) : ICommandHandler<SetSimAutoCommand, SimDaysView>
{
    public async ValueTask<Result<SimDaysView>> HandleAsync(SetSimAutoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await store.GetAsync(command.Id, cancellationToken) is not { } row)
        {
            return Result.Failure<SimDaysView>(SimDays.NotFound);
        }

        if (!await store.SetAutoAsync(row.Id, command.On, time.GetUtcNow(), cancellationToken))
        {
            return Result.Failure<SimDaysView>(Error.Conflict(SimDays.FinalCode, "The simulation is " + row.Phase + "; auto-run applies to an active one."));
        }

        return Result.Success(SimDaysSupport.View(await store.GetAsync(row.Id, cancellationToken) ?? row, await calendars.GetAsync(cancellationToken), options));
    }
}
