using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>Asks for the cursor day to be settled (SR-04, Instant): <c>idle</c> to <c>settling</c>; the runner settles it and moves it to <c>settled</c>. A repeat while it settles answers the same; a settled day must be advanced first (409 <c>sim.busy</c>).</summary>
internal sealed class SettleSimDayHandler(ISimDaysStore store, ITradingCalendar calendars, SimDaysOptions options, TimeProvider time) : ICommandHandler<SettleSimDayCommand, SimDaysView>
{
    public async ValueTask<Result<SimDaysView>> HandleAsync(SettleSimDayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await store.GetAsync(command.Id, cancellationToken) is not { } row)
        {
            return Result.Failure<SimDaysView>(SimDays.NotFound);
        }

        if (row.Phase == SimDays.Idle && !await store.MovePhaseAsync(row.Id, SimDays.Idle, SimDays.Settling, time.GetUtcNow(), cancellationToken))
        {
            return Result.Failure<SimDaysView>(SimDays.Busy("The simulation moved meanwhile; read it again."));
        }

        if (row.Phase is not (SimDays.Idle or SimDays.Settling))
        {
            return Result.Failure<SimDaysView>(row.Phase == SimDays.Settled ? SimDays.Busy("The day is settled; advance it first.") : Error.Conflict(SimDays.FinalCode, "The simulation is " + row.Phase + "."));
        }

        var now = await store.GetAsync(row.Id, cancellationToken) ?? row;
        return Result.Success(SimDaysSupport.View(now, await calendars.GetAsync(cancellationToken), options));
    }
}
