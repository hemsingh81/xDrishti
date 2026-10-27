using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>The fixed-format text of any plan date or week (spec P9-06 AC 12). Computes it from the facts and writes nothing; the model is not asked.</summary>
internal sealed class PreviewBriefingHandler(IBriefingFactSource source, IAssistantSettingsStore settings, BriefingOptions options, TimeProvider time) : IQueryHandler<PreviewBriefingQuery, Result<BriefingView>>
{
    public async ValueTask<Result<BriefingView>> HandleAsync(PreviewBriefingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var subject = await BriefingSubjects.ResolveAsync(query.Kind, query.Subject, source, time.GetUtcNow(), cancellationToken);
        if (subject.IsFailure)
        {
            return Result.Failure<BriefingView>(subject.Error);
        }

        var current = await settings.CurrentAsync(cancellationToken);
        var built = await source.BuildAsync(query.Kind, subject.Value, current.Enabled && current.PortfolioContext, cancellationToken);
        if (built.IsFailure)
        {
            return Result.Failure<BriefingView>(built.Error);
        }

        var facts = built.Value.Facts;
        var daily = query.Kind == BriefingFacts.Daily;
        var text = daily ? DeterministicBriefing.Build(facts, options.DailyMaxWords) : DeterministicWeeklyReview.Build(facts, options.WeeklyMaxWords);
        var json = BriefingFactsCodec.Json(facts);
        return Result.Success(new BriefingView(
            null, query.Kind, subject.Value, 0, "deterministic", BuildBriefingHandler.ReasonModelOff, text, text, TextGuard.WordCount(text), BriefingFactsCodec.Hash(json), null, "briefing." + PromptRegistry.Briefing.Version, [],
            new BriefingEvidenceView(facts.PlanId, facts.DigestId, facts.RunKeys), null, false));
    }
}

/// <summary>One stored text (spec P9-06 AC 12). Read only; 404 <c>assistant.briefing_not_found</c>.</summary>
internal sealed class GetBriefingHandler(IBriefingStore store, IBriefingFactSource source, TimeProvider time) : IQueryHandler<GetBriefingQuery, Result<BriefingView>>
{
    public async ValueTask<Result<BriefingView>> HandleAsync(GetBriefingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Subject is null && query.Kind == BriefingFacts.Daily)
        {
            var newest = await store.ListAsync(BriefingFacts.Daily, 1, cancellationToken);
            return newest.Count == 0 ? Result.Failure<BriefingView>(BriefingErrors.NotFound("No daily briefing is stored yet.")) : Result.Success(BriefingView.Of(newest[0], false));
        }

        var subject = await BriefingSubjects.ResolveAsync(query.Kind, query.Subject, source, time.GetUtcNow(), cancellationToken);
        if (subject.IsFailure)
        {
            return Result.Failure<BriefingView>(subject.Error);
        }

        var stored = await store.FindAsync(query.Kind, subject.Value, cancellationToken);
        return stored is null ? Result.Failure<BriefingView>(BriefingErrors.NotFound($"No {query.Kind} briefing is stored for {subject.Value}.")) : Result.Success(BriefingView.Of(stored, false));
    }
}

/// <summary>The stored texts, newest first (spec P9-06 Contract: limit 1 to 60, default 14). Read only.</summary>
internal sealed class ListBriefingsHandler(IBriefingStore store) : IQueryHandler<ListBriefingsQuery, Result<IReadOnlyList<BriefingView>>>
{
    public async ValueTask<Result<IReadOnlyList<BriefingView>>> HandleAsync(ListBriefingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 60)
        {
            return Result.Failure<IReadOnlyList<BriefingView>>(BriefingErrors.Invalid("limit", "The limit is 1 to 60."));
        }

        if (query.Kind is not null && !BriefingSubjects.KnownKind(query.Kind))
        {
            return Result.Failure<IReadOnlyList<BriefingView>>(BriefingErrors.Invalid("kind", "The kind is daily or weekly."));
        }

        var rows = await store.ListAsync(query.Kind, query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<BriefingView>>([.. rows.Select(r => BriefingView.Of(r, false))]);
    }
}
