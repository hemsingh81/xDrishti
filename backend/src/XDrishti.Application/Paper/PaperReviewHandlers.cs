using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>
/// Builds and stores the weekly paper review (spec P10-03, ADR 0121). It holds the read-only source, the review store and nothing else: no plan move, approval, setting, hypothesis or broker is reachable. The only write is the review text, once per week and epoch.
/// </summary>
internal sealed class BuildPaperReviewHandler(PaperReviewGatherer gatherer, IPaperReviewStore store, TimeProvider time, ILogger<BuildPaperReviewHandler> logger) : ICommandHandler<BuildPaperReviewCommand, PaperReviewView>
{
    public async ValueTask<Result<PaperReviewView>> HandleAsync(BuildPaperReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var week = command.Week is null ? PaperReviewWeek.Of(IndiaTime.Today(now)) : PaperReviewWeek.TryParse(command.Week) ?? default;
        if (command.Week is not null && week == default)
        {
            return Result.Failure<PaperReviewView>(PaperErrors.Invalid("week", "The week is written 2026-W43."));
        }

        if (week.IsFuture(now))
        {
            return Result.Failure<PaperReviewView>(PaperErrors.Invalid("week", "That week has not started."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            return Result.Failure<PaperReviewView>(PaperErrors.Invalid("actor", "The actor is required, at most 100 characters."));
        }

        if (command.Store && week.StoreRefusal(now) is { } refusal)
        {
            return Result.Failure<PaperReviewView>(PaperErrors.Invalid("store", refusal));
        }

        var window = await gatherer.ResolveAsync(command.EpochId, week.Start, week.End, cancellationToken);
        if (window.IsFailure)
        {
            return Result.Failure<PaperReviewView>(window.Error);
        }

        var w = window.Value;
        if (w.Epoch is null)
        {
            var empty = PaperReviewBuilder.Build(Input(week, now, w, null, null, null, string.Empty));
            if (command.Store)
            {
                return Result.Failure<PaperReviewView>(PaperErrors.Invalid("store", "There is no paper epoch yet, so there is no review to store."));
            }

            return Result.Success(new PaperReviewView(null, week.Key, null, now, null, false, false, empty.Text, empty.Sections));
        }

        if (command.Store && await store.FindAsync(week.Key, w.Epoch.Id, cancellationToken) is { } existing)
        {
            PaperLog.ReviewRepeat(logger, week.Key, w.Epoch.Id, existing.Id);
            return Result.Success(Of(existing, true));
        }

        var (facts, why) = await gatherer.RecordAsync(w, cancellationToken);
        var line = await gatherer.ComparisonAsync(w.Epoch.Id, cancellationToken);
        var review = PaperReviewBuilder.Build(Input(week, now, w, facts, why, PaperReviewGatherer.Config(w, week.Start, week.End), line));
        if (!command.Store)
        {
            PaperLog.ReviewDryRun(logger, week.Key, w.Epoch.Id);
            return Result.Success(new PaperReviewView(null, week.Key, w.Epoch.Id, now, null, false, false, review.Text, review.Sections));
        }

        var (stored, created) = await store.InsertAsync(new NewPaperReview(week.Key, w.Epoch.Id, now, command.Actor, review.Text, review.Sections), cancellationToken);
        if (created)
        {
            PaperLog.ReviewStored(logger, week.Key, w.Epoch.Id, stored.Id, review.Sections.Count);
        }
        else
        {
            PaperLog.ReviewRepeat(logger, week.Key, w.Epoch.Id, stored.Id);
        }

        return Result.Success(Of(stored, !created));
    }

    /// <summary>The view of a stored review.</summary>
    internal static PaperReviewView Of(StoredPaperReview s, bool repeated) =>
        new(s.Id, s.Review.Week, s.Review.EpochId, s.Review.BuiltAt, s.Review.BuiltBy, true, repeated, s.Review.Text, s.Review.Sections);

    private static PaperReviewInput Input(PaperReviewWeek week, DateTimeOffset now, PaperReviewGatherer.Window w, PaperRecordFacts? facts, string? why, ReviewConfig? config, string comparison) =>
        new(week, now, w.Epoch, w.From, w.To, w.SessionsInRange, w.Sessions.Count, facts, why, config, null, comparison);
}

/// <summary>One stored paper review (spec P10-03). Read only.</summary>
internal sealed class GetPaperReviewHandler(IPaperReviewSource source, IPaperReviewStore store) : IQueryHandler<GetPaperReviewQuery, Result<PaperReviewView>>
{
    public async ValueTask<Result<PaperReviewView>> HandleAsync(GetPaperReviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (PaperReviewWeek.TryParse(query.IsoWeek) is not { } week)
        {
            return Result.Failure<PaperReviewView>(PaperErrors.Invalid("week", "The week is written 2026-W43."));
        }

        var epochs = await source.EpochsAsync(cancellationToken);
        var epoch = query.EpochId is null ? (epochs.Count > 0 ? epochs[0] : null) : epochs.FirstOrDefault(e => e.Epoch.Id == query.EpochId);
        var stored = epoch is null ? null : await store.FindAsync(week.Key, epoch.Epoch.Id, cancellationToken);
        return stored is null
            ? Result.Failure<PaperReviewView>(Error.NotFound(PaperErrors.NotFoundCode, $"No paper review is stored for {week.Key}."))
            : Result.Success(BuildPaperReviewHandler.Of(stored, false));
    }
}

/// <summary>The stored paper reviews, newest week first (spec P10-03). Read only.</summary>
internal sealed class ListPaperReviewsHandler(IPaperReviewStore store) : IQueryHandler<ListPaperReviewsQuery, Result<PaperReviewList>>
{
    public async ValueTask<Result<PaperReviewList>> HandleAsync(ListPaperReviewsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 52)
        {
            return Result.Failure<PaperReviewList>(PaperErrors.Invalid("limit", "The limit is 1 to 52."));
        }

        var rows = await store.ListAsync(query.EpochId, query.Limit, cancellationToken);
        return Result.Success(new PaperReviewList([.. rows.Select(r => new PaperReviewListItem(r.Id, r.Review.Week, r.Review.EpochId, r.Review.BuiltAt))]));
    }
}

/// <summary>The discipline report of a window (spec P10-03). Reads stored facts; writes nothing.</summary>
internal sealed class GetDisciplineHandler(PaperReviewGatherer gatherer, ILogger<GetDisciplineHandler> logger) : IQueryHandler<GetDisciplineQuery, Result<DisciplineView>>
{
    public async ValueTask<Result<DisciplineView>> HandleAsync(GetDisciplineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From is { } f && query.To is { } t && f > t)
        {
            return Result.Failure<DisciplineView>(PaperErrors.Invalid("from", "The first day is after the last day."));
        }

        var window = await gatherer.ResolveAsync(query.EpochId, query.From ?? DateOnly.MinValue, query.To ?? DateOnly.MaxValue, cancellationToken);
        if (window.IsFailure)
        {
            return Result.Failure<DisciplineView>(window.Error);
        }

        var w = window.Value;
        if (w.Epoch is null)
        {
            return Result.Success(DisciplineViews.Empty(null, w.From, w.To, "no paper epoch exists yet"));
        }

        var (facts, why) = await gatherer.RecordAsync(w, cancellationToken);
        if (facts is null)
        {
            return Result.Success(DisciplineViews.Empty(w.Epoch.Id, w.From, w.To, why));
        }

        var report = DisciplineReport.Build(facts.Tickets, facts.Trades, facts.Skips, facts.Fills, facts.FillsUnavailable);
        PaperLog.DisciplineRead(logger, w.Sessions.Count, report.PrimaryCount);
        return Result.Success(DisciplineViews.Of(w.Epoch.Id, w.From, w.To, w.Sessions.Count, report));
    }
}
