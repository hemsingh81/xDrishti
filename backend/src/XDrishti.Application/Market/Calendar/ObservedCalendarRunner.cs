using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>What the observed pass did. <see cref="Skipped"/>: nothing was concluded because some bars were not aggregated yet.</summary>
internal readonly record struct ObservedRun(bool Skipped, int Added, int Updated, int Removed);

/// <summary>
/// Proposes the days the stored bars suggest are holidays or special sessions (<see cref="ObservedProposals"/>) and keeps the proposals up to date: new ones are added,
/// ones the bars no longer support are withdrawn. Proposals are never confirmed here. Runs after every data job; skipped while bars are waiting to be aggregated
/// (the daily rows would be out of date and every freshly loaded day would look closed).
/// </summary>
internal sealed class ObservedCalendarRunner(
    IObservedDays observed,
    ITradingDayStore store,
    ITradingCalendar calendar,
    IMarketUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ObservedCalendarRunner> logger)
{
    public async ValueTask<ObservedRun> RunAsync(CancellationToken cancellationToken)
    {
        var presence = await observed.ReadPresenceAsync(cancellationToken);
        if (presence.Pending)
        {
            CalendarLog.ObservedSkipped(logger);
            return new ObservedRun(true, 0, 0, 0);
        }

        var existing = await store.ListAsync(cancellationToken);
        var holidays = existing.Where(d => d.IsConfirmed && d.Kind == CalendarDayKind.Holiday).Select(d => d.Id).ToHashSet();
        var candidates = ObservedProposals.WindowCandidates(presence, holidays);
        var windows = candidates.Count == 0 ? new Dictionary<DateOnly, ObservedWindow>() : new Dictionary<DateOnly, ObservedWindow>(await observed.ReadWindowsAsync(candidates, cancellationToken));
        var proposals = ObservedProposals.Build(presence, windows, holidays);

        var counts = CalendarMerge.ApplyObserved(proposals, existing, store, clock.GetUtcNow());
        if (!counts.Changed)
        {
            return new ObservedRun(false, 0, 0, 0);
        }

        if (!await unitOfWork.TrySaveAsync(cancellationToken))
        {
            CalendarLog.SaveCollided(logger);
            return new ObservedRun(false, 0, 0, 0);
        }

        calendar.Invalidate();
        CalendarLog.ObservedApplied(logger, counts.Added, counts.Updated, counts.Removed);
        return new ObservedRun(false, counts.Added, counts.Updated, counts.Removed);
    }
}
