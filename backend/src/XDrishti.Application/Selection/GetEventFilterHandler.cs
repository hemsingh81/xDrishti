using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Selection;

/// <summary>
/// <c>GET /api/selection/event-filter</c> (P15-06): the effective switch, its settings, the latest 50 rows and why the filter is not acting (switch off, no calendar rows, no entry for N tracked instruments). A history or calendar that cannot be read is said so and
/// reads as OFF / unavailable, never as an error page and never as "acting". Read only.
/// </summary>
internal sealed class GetEventFilterHandler(IEventFilterDecisions decisions, IResultsCalendar calendar, IFilingInstruments instruments, IEventFilterVerdict verdicts, TimeProvider clock) : IQueryHandler<GetEventFilterQuery, EventFilterView>
{
    public async ValueTask<EventFilterView> HandleAsync(GetEventFilterQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reasons = new List<EventFilterReasonView>();
        IReadOnlyList<EventFilterRow> rows = [];
        try
        {
            rows = await decisions.RowsAsync(EventFilterSettings.HistorySize, cancellationToken);
        }
#pragma warning disable CA1031 // An unreadable history reads as OFF and says so; cancellation is rethrown by the filter.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            _ = failure;
            reasons.Add(new EventFilterReasonView("history_unreadable", null));
        }

        var state = EventFilterSettings.StateOf(rows);
        if (!state.Enabled)
        {
            reasons.Add(new EventFilterReasonView("switch_off", null));
        }

        int? tracked = null;
        int? withEntry = null;
        try
        {
            var all = await instruments.TrackedAsync(cancellationToken);
            var answers = all.Count == 0 ? new Dictionary<Guid, ResultsAnswer>() : new Dictionary<Guid, ResultsAnswer>(await calendar.DaysToResults([.. all.Select(i => i.Id)], clock.GetUtcNow(), cancellationToken));
            tracked = all.Count;
            withEntry = all.Count(i => answers.TryGetValue(i.Id, out var a) && ResultsWindow.DaysOrNull(a) is not null);
            // Fail closed (ADR 0182): an instrument whose last meeting has passed and whose next is not yet intimated is unavailable too, and is counted on its own so the share can be reported.
            var noUpcoming = all.Count(i => answers.TryGetValue(i.Id, out var a) && a.State == ResultsAnswer.Unavailable && a.Reason == ResultsAnswer.NoUpcomingEntry);
            if (withEntry == 0 && noUpcoming == 0)
            {
                reasons.Add(new EventFilterReasonView("no_calendar_rows", null));
            }
            else
            {
                if (tracked - withEntry - noUpcoming > 0)
                {
                    reasons.Add(new EventFilterReasonView("no_entry_for_instruments", tracked - withEntry - noUpcoming));
                }

                if (noUpcoming > 0)
                {
                    reasons.Add(new EventFilterReasonView("no_upcoming_entry_for_instruments", noUpcoming));
                }
            }
        }
#pragma warning disable CA1031 // An unreadable calendar is said so; cancellation is rethrown by the filter.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            _ = failure;
            reasons.Add(new EventFilterReasonView("calendar_unreadable", null));
        }

        var verdict = "unavailable";
        try
        {
            if (await verdicts.NewestAsync(EventCalendarSnapshot.RuleVersion, cancellationToken) is { } newest && !string.IsNullOrWhiteSpace(newest.State))
            {
                verdict = newest.State;
            }
        }
#pragma warning disable CA1031 // An unreadable verdict is unavailable; cancellation is rethrown by the filter.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            _ = failure;
        }

        return new EventFilterView(state.Enabled, state.ResultsWithinDays, state.Approval, state.DecidedBy, verdict, [.. rows.Select(EventFilterSettings.View)], reasons, tracked, withEntry);
    }
}
