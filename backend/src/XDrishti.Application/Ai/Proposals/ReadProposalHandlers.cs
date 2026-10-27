using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Proposals;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>One proposal with its decision (spec P9-08 Contract). Read only; works while generation is off.</summary>
internal sealed class GetProposalHandler(IProposalReader reader, TimeProvider time) : IQueryHandler<GetProposalQuery, Result<ProposalView>>
{
    public async ValueTask<Result<ProposalView>> HandleAsync(GetProposalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var found = await reader.GetAsync(query.Id, cancellationToken);
        return found is null ? Result.Failure<ProposalView>(ProposalErrors.NotFound(query.Id)) : Result.Success(ProposalView.Of(found, time.GetUtcNow()));
    }
}

/// <summary>The proposals, newest first, optionally of one state and kind (spec P9-08 Contract). The state is computed on read: a pending proposal whose expiry has passed is expired. Read only; works while generation is off.</summary>
internal sealed class ListProposalsHandler(IProposalReader reader, TimeProvider time) : IQueryHandler<ListProposalsQuery, Result<IReadOnlyList<ProposalView>>>
{
    private const int Window = 500;

    public async ValueTask<Result<IReadOnlyList<ProposalView>>> HandleAsync(ListProposalsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > ListProposalsQuery.MaxLimit)
        {
            return Result.Failure<IReadOnlyList<ProposalView>>(ProposalErrors.Invalid($"The limit is 1 to {ListProposalsQuery.MaxLimit}."));
        }

        if (query.State is not null && !ProposalLifecycle.States.Contains(query.State, StringComparer.Ordinal))
        {
            return Result.Failure<IReadOnlyList<ProposalView>>(ProposalErrors.Invalid("The state is pending, accepted, rejected or expired."));
        }

        if (query.Kind is not null && !ProposalKind.IsKnown(query.Kind))
        {
            return Result.Failure<IReadOnlyList<ProposalView>>(ProposalErrors.Invalid("The kind is hypothesis, basket_add or basket_remove."));
        }

        var now = time.GetUtcNow();
        var rows = await reader.ListAsync(query.Kind, Window, cancellationToken);
        var views = rows.Select(r => ProposalView.Of(r, now)).Where(v => query.State is null || v.State == query.State).Take(query.Limit).ToList();
        return Result.Success<IReadOnlyList<ProposalView>>(views);
    }
}

/// <summary>Whether accepting is switched on now.</summary>
internal sealed class ProposalAcceptAllowedHandler(IAssistantSettingsStore settings) : IQueryHandler<ProposalAcceptAllowedQuery, bool>
{
    public async ValueTask<bool> HandleAsync(ProposalAcceptAllowedQuery query, CancellationToken cancellationToken) => (await settings.CurrentAsync(cancellationToken)).AllowProposalAccept;
}
