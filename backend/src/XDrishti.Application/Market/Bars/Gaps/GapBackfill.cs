using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Bars.Gaps;

/// <summary>
/// The gap backfill of the Dhan phase (ADR 0017 §3). <see cref="PlanAsync"/>, before an instrument is fetched: finds its sessions (the last <c>Data:Gaps:LookbackSessions</c> after
/// its first bar, plus the gaps still on record) with fewer 1-minute bars than the calendar says, records them and asks for the older ones in extra windows.
/// <see cref="CompleteAsync"/>, after its bars are stored: counts again and decides — all bars there: filled; more, but not all: still open; nothing more: unfillable (scheduled
/// runs leave it alone, "Re-fetch gaps" tries again). An instrument whose fetch failed is not completed, so nothing is decided and the next run tries again. The counts come
/// from the daily aggregates (<see cref="IBarDayCounts"/>), never from a scan of the 1-minute bars.
/// </summary>
internal sealed class GapBackfill(IBarDayCounts dayCounts, IBarGaps gaps, IDataSettings settings, TimeProvider clock)
{
    public async ValueTask<GapPlan> PlanAsync(TradingCalendar calendar, FetchTarget target, bool retryUnfillable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(target);
        if (target.FirstBar is null || target.LastBar is null)
        {
            return GapPlan.None;
        }

        var now = clock.GetUtcNow();
        var sessions = GapSessions.Recent(calendar, now, settings.GapLookbackSessions, target.FirstBar);
        var since = sessions.Count > 0 ? sessions[^1] : DateOnly.MaxValue;
        var known = (await gaps.ListForAsync(target.InstrumentId, since, cancellationToken)).ToDictionary(g => g.Date);
        var dates = sessions.Union(known.Keys).Order().ToList();
        if (dates.Count == 0)
        {
            return GapPlan.None;
        }

        var counts = await dayCounts.CountAsync(target.InstrumentId, dates[0], dates[^1], cancellationToken);
        var ordinaryFrom = IndiaTime.Today(DhanFetchPlanner.StartOf(target.LastBar, now, DhanFetchPlanner.InitialDaysOf(target.Type, settings.DhanInitialDays, settings.DhanInitialDaysIndex)));
        var recent = sessions.ToHashSet();
        var items = new List<GapItem>();
        var deferred = new List<DeferredDay>();
        var save = new List<BarGap>();
        foreach (var date in dates)
        {
            var expected = calendar.ExpectedMinutes(date);
            var found = counts.GetValueOrDefault(date);
            if (known.TryGetValue(date, out var gap))
            {
                if (gap.Refresh(expected, found, now))
                {
                    save.Add(gap);
                }

                if (gap.Status != GapStatus.Filled)
                {
                    items.Add(new GapItem(gap, found, date >= ordinaryFrom || gap.Status == GapStatus.Open || retryUnfillable));
                }
            }
            else if (recent.Contains(date) && !BarGap.IsComplete(expected, found))
            {
                if (date >= ordinaryFrom)
                {
                    deferred.Add(new DeferredDay(date, expected, found)); // the ordinary fetch covers it: a gap only if that does not complete it
                }
                else
                {
                    var created = BarGap.Detect(target.InstrumentId, date, expected, found, now);
                    save.Add(created);
                    items.Add(new GapItem(created, found, true));
                }
            }
        }

        if (save.Count > 0)
        {
            await gaps.SaveAsync(save, cancellationToken);
        }

        var older = items.Where(i => i.Refetch && i.Gap.Date < ordinaryFrom).Select(i => i.Gap.Date).Order().ToList();
        return new GapPlan(DhanFetchPlanner.GapWindows(older, calendar, now), items, deferred);
    }

    public async ValueTask<GapOutcome> CompleteAsync(Guid instrumentId, GapPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var asked = plan.Items.Where(i => i.Refetch).ToList();
        if (asked.Count == 0 && plan.Deferred.Count == 0)
        {
            return GapOutcome.None;
        }

        var dates = asked.Select(i => i.Gap.Date).Concat(plan.Deferred.Select(d => d.Date)).ToList();
        var counts = await dayCounts.CountAsync(instrumentId, dates.Min(), dates.Max(), cancellationToken);
        var now = clock.GetUtcNow();
        var save = new List<BarGap>();
        foreach (var item in asked)
        {
            item.Gap.RecordRefetch(item.FoundBefore, counts.GetValueOrDefault(item.Gap.Date), now);
            save.Add(item.Gap);
        }

        foreach (var day in plan.Deferred)
        {
            var after = counts.GetValueOrDefault(day.Date);
            if (!BarGap.IsComplete(day.Expected, after))
            {
                var gap = BarGap.Detect(instrumentId, day.Date, day.Expected, day.Found, now);
                gap.RecordRefetch(day.Found, after, now);
                save.Add(gap);
            }
        }

        // Sessions the ordinary window completed were never gaps and are not recorded; everything in save is tallied by its new state.
        await gaps.SaveAsync(save, cancellationToken);
        return new GapOutcome(
            save.Count,
            save.Count(g => g.Status == GapStatus.Filled),
            save.Count(g => g.Status == GapStatus.Unfillable),
            save.Count(g => g.Status == GapStatus.Open));
    }
}
