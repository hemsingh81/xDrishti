using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Calendar;

/// <summary>
/// Reads NSE's public holiday list now (one request, best effort). The answer is always a success with a <see cref="CalendarRefreshResult"/>: when NSE refuses or
/// the list cannot be read, <c>succeeded</c> is false with the reason and nothing changes — add holidays by hand then. Audited when something changed.
/// </summary>
public sealed record RefreshCalendarCommand(string Actor) : ICommand;

internal sealed class RefreshCalendarHandler(NseCalendarRefresher refresher, HolidayFetchGate gate, TimeProvider clock) : ICommandHandler<RefreshCalendarCommand, CalendarRefreshResult>
{
    public async ValueTask<Result<CalendarRefreshResult>> HandleAsync(RefreshCalendarCommand command, CancellationToken cancellationToken)
    {
        gate.TryBegin(IndiaTime.Today(clock.GetUtcNow())); // the automatic attempt of the day is not needed after the owner asked
        return Result.Success(await refresher.RefreshAsync(command.Actor, cancellationToken));
    }
}
