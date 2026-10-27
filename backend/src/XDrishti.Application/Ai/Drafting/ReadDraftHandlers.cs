using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>One draft: the stored row (or the job still in progress), the owner's decision and, once the linked quick test has a state, its deterministic summary (spec P9-07 AC 9). Read only.</summary>
internal sealed class GetDraftHandler(IDraftStore store, ILabRunStore runs, DraftJobs jobs) : IQueryHandler<GetDraftQuery, Result<DraftView>>
{
    public async ValueTask<Result<DraftView>> HandleAsync(GetDraftQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var stored = await store.GetAsync(query.Id, cancellationToken);
        if (stored is null)
        {
            return jobs.IdeaOf(query.Id) is { } idea ? Result.Success(DraftView.InProgress(query.Id, idea)) : Result.Failure<DraftView>(DraftErrors.NotFound(query.Id));
        }

        return Result.Success(await ViewAsync(stored, store, runs, cancellationToken));
    }

    internal static async ValueTask<DraftView> ViewAsync(StoredDraft stored, IDraftStore store, ILabRunStore runs, CancellationToken cancellationToken)
    {
        var decision = await store.DecisionAsync(stored.Id, cancellationToken);
        var run = decision?.LabRunId is { } runId ? await runs.GetAsync(runId, cancellationToken) : null;
        return DraftView.Of(stored, decision, run);
    }
}

/// <summary>The newest drafts with their decisions (limit 1 to 50). Read only.</summary>
internal sealed class ListDraftsHandler(IDraftStore store, ILabRunStore runs) : IQueryHandler<ListDraftsQuery, Result<IReadOnlyList<DraftView>>>
{
    public async ValueTask<Result<IReadOnlyList<DraftView>>> HandleAsync(ListDraftsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > ListDraftsQuery.MaxLimit)
        {
            return Result.Failure<IReadOnlyList<DraftView>>(DraftErrors.Invalid($"limit is 1 to {ListDraftsQuery.MaxLimit}."));
        }

        var list = new List<DraftView>();
        foreach (var draft in await store.ListAsync(query.Limit, cancellationToken))
        {
            list.Add(await GetDraftHandler.ViewAsync(draft, store, runs, cancellationToken));
        }

        return Result.Success<IReadOnlyList<DraftView>>(list);
    }
}

/// <summary>Whether the switch <c>ai.settings.allow_draft_confirm</c> is on now.</summary>
internal sealed class DraftConfirmAllowedHandler(IAssistantSettingsStore settings) : IQueryHandler<DraftConfirmAllowedQuery, bool>
{
    public async ValueTask<bool> HandleAsync(DraftConfirmAllowedQuery query, CancellationToken cancellationToken) => (await settings.CurrentAsync(cancellationToken)).AllowDraftConfirm;
}
