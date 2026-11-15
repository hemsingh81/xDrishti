using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>Cancels a simulation; its stored days stay. A cancelled or failed run: 409 <c>sim.final</c>.</summary>
internal sealed class CancelSimDaysHandler(ISimDaysStore store, ITradingCalendar calendars, SimDaysOptions options, TimeProvider time, ILogger<CancelSimDaysHandler> logger) : ICommandHandler<CancelSimDaysCommand, SimDaysView>
{
    public async ValueTask<Result<SimDaysView>> HandleAsync(CancelSimDaysCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await store.GetAsync(command.Id, cancellationToken) is not { } row)
        {
            return Result.Failure<SimDaysView>(SimDays.NotFound);
        }

        if (!await store.CancelAsync(row.Id, time.GetUtcNow(), cancellationToken))
        {
            return Result.Failure<SimDaysView>(Error.Conflict(SimDays.FinalCode, "The simulation is " + row.Phase + "."));
        }

        SimDaysLog.Cancelled(logger, row.Id);
        return Result.Success(SimDaysSupport.View(await store.GetAsync(row.Id, cancellationToken) ?? row, await calendars.GetAsync(cancellationToken), options));
    }
}
