using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The halt, capture and loss facts of a live account for the session being planned (spec P11-04 criterion 4, ADR 0151). A daily halt is in force on its own day; a weekly <c>stop_new</c> halt on the rest of its week; a weekly <c>halve</c> halves the scale of the
/// next ISO week. The capture and the loss are those of the last completed session; a session before the account's first step is not judged (nothing was captured then). A capture that is not healthy makes the loss unmeasurable: both fail the gate, never read as zero.
/// Reads stored data only.
/// </summary>
internal sealed class LiveReadyFactsSource(ILiveHaltStore halts, IRampStore ramp, LiveLossReader loss, ITradingCalendar calendars, TimeProvider time) : ILiveReadyFactsSource
{
    public async ValueTask<LiveStanding> ForAsync(Guid accountId, DateOnly forDate, bool stepSet, CancellationToken cancellationToken)
    {
        if (!stepSet)
        {
            return new LiveStanding(new LiveReadyFacts(false, null, null, null), 0);
        }

        var today = IndiaTime.Today(time.GetUtcNow());
        var rows = (await halts.ListAsync(accountId, forDate.AddDays(-14), forDate, cancellationToken)).Where(h => h.Event == HaltCodes.Opened).ToList();
        var stopped = rows.Any(h => (h.Kind == HaltCodes.Daily && h.PeriodStart == forDate) || (h.Kind == HaltCodes.Weekly && h.Action == HaltCodes.StopNew && RampAssessor.WeekStart(forDate) == h.PeriodStart));
        var halved = rows.Any(h => h.Kind == HaltCodes.Weekly && h.Action == HaltCodes.Halve && RampAssessor.WeekStart(forDate) == h.PeriodStart.AddDays(7));
        var factor = stopped ? 0 : halved ? 0.5 : 1.0;

        var calendar = await calendars.GetAsync(cancellationToken);
        var last = forDate.AddDays(-1);
        for (var i = 0; i < 10 && !(calendar.IsTradingDay(last) && last <= today); i++)
        {
            last = last.AddDays(-1);
        }

        var first = (await ramp.HistoryAsync(accountId, 200, cancellationToken)) is { Count: > 0 } all ? all[^1] : null;
        if (first is null || last <= IndiaTime.Today(first.CreatedAt) || !calendar.IsTradingDay(last))
        {
            return new LiveStanding(new LiveReadyFacts(true, !stopped, true, true), factor);
        }

        var measure = await loss.MeasureAsync(accountId, last, cancellationToken);
        return new LiveStanding(new LiveReadyFacts(true, !stopped, measure.Health == CaptureHealth.Healthy, measure.Loss.State != MeasureStates.Unavailable), factor);
    }
}
