using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>
/// What the review handlers share (spec P10-03): resolving the epoch and its window, and reading the stored facts of the window through <see cref="IPaperReviewSource"/> (reads only). A source that cannot be read comes back as a reason, never as a thrown
/// error and never as zeros.
/// </summary>
internal sealed class PaperReviewGatherer(IPaperReviewSource source, ITradingCalendar calendars, IReviewComparisonLine comparison, TimeProvider time, ILogger<PaperReviewGatherer> logger)
{
    /// <summary>The most sessions one window may hold (a 12-week epoch has about 60).</summary>
    public const int MaxSessions = 130;

    /// <summary>The epoch, the days of the window and the sessions of it.</summary>
    /// <param name="Epoch">The epoch, or null when none exists.</param>
    /// <param name="Summaries">All epochs, newest first.</param>
    /// <param name="From">First day of the window.</param>
    /// <param name="To">Last day of the window.</param>
    /// <param name="Sessions">Closed sessions of the window.</param>
    /// <param name="SessionsInRange">All trading sessions of the requested range inside the epoch.</param>
    public sealed record Window(ReviewEpoch? Epoch, IReadOnlyList<EpochSummary> Summaries, DateOnly From, DateOnly To, IReadOnlyList<DateOnly> Sessions, int SessionsInRange);

    public async ValueTask<Result<Window>> ResolveAsync(long? epochId, DateOnly rangeFrom, DateOnly rangeTo, CancellationToken cancellationToken)
    {
        var summaries = await source.EpochsAsync(cancellationToken);
        var chosen = epochId is null ? (summaries.Count > 0 ? summaries[0] : null) : summaries.FirstOrDefault(s => s.Epoch.Id == epochId);
        if (epochId is not null && chosen is null)
        {
            return Result.Failure<Window>(PaperErrors.NotFound("paper epoch with this id"));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(time.GetUtcNow());
        if (chosen is null)
        {
            return Result.Success(new Window(null, summaries, lastClosed, lastClosed, [], 0));
        }

        var epoch = new ReviewEpoch(chosen.Epoch.Id, chosen.Epoch.StartedOn, chosen.Epoch.Kind, chosen.Epoch.Reason);
        var next = summaries.Where(s => s.Epoch.StartedOn > epoch.StartedOn).OrderBy(s => s.Epoch.StartedOn).FirstOrDefault();
        var from = rangeFrom > epoch.StartedOn ? rangeFrom : epoch.StartedOn;
        var end = DateOnly.FromDayNumber(Math.Min(Math.Min(rangeTo.DayNumber, lastClosed.DayNumber + 7), next is null ? int.MaxValue : next.Epoch.StartedOn.DayNumber - 1));
        var inRange = new List<DateOnly>();
        for (var day = from; day <= end; day = day.AddDays(1))
        {
            if (calendar.IsTradingDay(day))
            {
                inRange.Add(day);
            }
        }

        var closed = inRange.Where(d => d <= lastClosed).ToList();
        var to = end < lastClosed ? end : lastClosed;
        return Result.Success(new Window(epoch, summaries, from, to < from ? from : to, closed, inRange.Count));
    }

    public async ValueTask<(PaperRecordFacts? Facts, string? Why)> RecordAsync(Window window, CancellationToken cancellationToken)
    {
        if (window.Sessions.Count > MaxSessions)
        {
            return (null, $"the window holds more than {MaxSessions} sessions");
        }

        try
        {
            var tickets = new List<ReviewTicket>();
            foreach (var session in window.Sessions)
            {
                tickets.AddRange(await source.TicketsAsync(session, cancellationToken));
            }

            var trades = await source.TradesAsync(window.Epoch!.Id, cancellationToken);
            var skips = await source.SkipsAsync(window.Epoch.Id, cancellationToken);
            var (fills, fillsWhy) = await FillsAsync(window.Sessions, cancellationToken);
            return (new PaperRecordFacts(tickets, trades, skips, fills, fillsWhy), null);
        }
#pragma warning disable CA1031 // A source that cannot be read becomes an 'unavailable' section; cancellation is rethrown by the filter.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return (null, Unreadable("paper record", failure));
        }
    }

    public async ValueTask<string> ComparisonAsync(long epochId, CancellationToken cancellationToken)
    {
        try
        {
            return await comparison.LineAsync(epochId, cancellationToken);
        }
#pragma warning disable CA1031 // The comparison is one line; its failure must not stop the review.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return "unavailable: " + Unreadable("backtest comparison", failure);
        }
    }

    public static ReviewConfig Config(Window window, DateOnly weekStart, DateOnly weekEnd)
    {
        var epoch = window.Summaries.First(s => s.Epoch.Id == window.Epoch!.Id);
        var started = window.Summaries.Where(s => s.Epoch.StartedOn >= weekStart && s.Epoch.StartedOn <= weekEnd)
            .OrderBy(s => s.Epoch.Id).Select(s => new ReviewEpoch(s.Epoch.Id, s.Epoch.StartedOn, s.Epoch.Kind, s.Epoch.Reason)).ToList();
        return new ReviewConfig(started, epoch.RiskDownChanges, epoch.UnexplainedChanges);
    }

    private async ValueTask<(IReadOnlyList<ReviewFill>? Fills, string? Why)> FillsAsync(IReadOnlyList<DateOnly> sessions, CancellationToken cancellationToken)
    {
        try
        {
            var all = new List<ReviewFill>();
            foreach (var session in sessions)
            {
                all.AddRange(await source.DiscretionaryAsync(session, cancellationToken));
            }

            return (all, null);
        }
#pragma warning disable CA1031 // The journal may be unreadable; the added line then says so and the rest is still produced.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return (null, Unreadable("journal", failure));
        }
    }

    /// <summary>Only the exception type is kept: a database message can carry values, and neither the log nor the stored text may.</summary>
    private string Unreadable(string name, Exception failure)
    {
        PaperLog.ReviewSourceUnavailable(logger, name, failure.GetType().Name);
        return $"the {name} could not be read ({failure.GetType().Name})";
    }
}
