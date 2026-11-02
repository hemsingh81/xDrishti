using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Planning;

/// <summary>
/// What the event-risk filter knows for one plan build (P15-06, ADR 0182). <see cref="Active"/> is false when the switch is OFF, unreadable (OFF) or the filter did not run: then the plan is exactly what it was before (<c>ExcludedByEvent</c> and <c>DaysToResults</c> stay null).
/// When active, <see cref="Answers"/> holds the P15-04 answer per instrument; an instrument without an answer, or a calendar that could not be read, is <c>unavailable</c> and never a pass.
/// </summary>
internal sealed record PlanEventFacts(bool Active, int Days, IReadOnlyDictionary<Guid, ResultsAnswer> Answers, int Queried, int WithEntry, bool ReadFailed, int NoUpcoming = 0)
{
    public static PlanEventFacts Inactive { get; } = new(false, ResultsWindow.DefaultDays, new Dictionary<Guid, ResultsAnswer>(), 0, 0, false);

    public int Unavailable => Queried - WithEntry;

    /// <summary>The value for <c>PlanCandidate.ExcludedByEvent</c>: null when inactive, when the candidate has no instrument id or no answer, or when its answer is unavailable.</summary>
    public bool? ExcludedFor(Guid? instrumentId) =>
        Active && instrumentId is { } id && Answers.TryGetValue(id, out var answer) ? ResultsWindow.ExcludedByEvent(answer, Days) : null;

    /// <summary>The sessions to the next results for the plan check; null when inactive or unavailable.</summary>
    public int? DaysFor(Guid? instrumentId) =>
        Active && instrumentId is { } id && Answers.TryGetValue(id, out var answer) ? ResultsWindow.DaysOrNull(answer) : null;

    /// <summary>The sentence the plan basis carries when the filter is active, for example <c>events: N=2, 187 of 214 instruments with an entry, 27 unavailable</c>.</summary>
    public string? Note => !Active
        ? null
        : ReadFailed
            ? "events: the results calendar could not be read, every instrument unavailable"
            : string.Create(CultureInfo.InvariantCulture, $"events: N={Days}, {WithEntry} of {Queried} instruments with an entry, {Unavailable} unavailable")
                + (NoUpcoming > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({NoUpcoming} of them with no upcoming meeting announced yet)") : string.Empty);
}

/// <summary>
/// Reads the switch and, when it is ON, one batched <see cref="IResultsCalendar"/> answer for the instruments of a plan. The question is asked at the build instant, or at the start of the plan's India date when the plan is built earlier (the stored rows
/// are what was ingested by the build, so nothing later can be in them); never later than the last instant of the plan's India date (a rebuild after that date still counts sessions from the plan's own date); sessions are counted from the plan's date. A read that fails is logged by type and answers unavailable for everyone.
/// </summary>
internal sealed class PlanEventFilter(IEventFilterSwitch switchState, IResultsCalendar calendar, ILogger<PlanEventFilter> logger)
{
    public async ValueTask<PlanEventFacts> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly forDate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var state = await switchState.CurrentAsync(cancellationToken);
        if (!state.Enabled)
        {
            return PlanEventFacts.Inactive;
        }

        var ids = instrumentIds.Distinct().ToList();
        var start = new DateTimeOffset(forDate.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        var end = start.AddDays(1).AddTicks(-10);
        var t = now < start ? start : now > end ? end : now;
        try
        {
            var answers = ids.Count == 0 ? new Dictionary<Guid, ResultsAnswer>() : new Dictionary<Guid, ResultsAnswer>(await calendar.DaysToResults(ids, t, cancellationToken));
            var withEntry = ids.Count(id => answers.TryGetValue(id, out var a) && ResultsWindow.DaysOrNull(a) is not null);
            if (ids.Count > withEntry)
            {
                EventFilterLog.Unavailable(logger, forDate, ids.Count - withEntry, "no_entry");
            }

            var noUpcoming = ids.Count(id => answers.TryGetValue(id, out var a) && a.State == ResultsAnswer.Unavailable && a.Reason == ResultsAnswer.NoUpcomingEntry);
            return new PlanEventFacts(true, state.ResultsWithinDays, answers, ids.Count, withEntry, false, noUpcoming);
        }
#pragma warning disable CA1031 // A failing calendar read makes every answer unavailable (the safe path); cancellation is rethrown by the filter. Only the type of the failure is logged.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventFilterLog.CalendarReadFailed(logger, failure.GetType().Name);
            EventFilterLog.Unavailable(logger, forDate, ids.Count, "read_failed");
            return new PlanEventFacts(true, state.ResultsWithinDays, new Dictionary<Guid, ResultsAnswer>(), ids.Count, 0, true);
        }
    }
}
