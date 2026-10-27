using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Ai;

/// <summary>Answers the question of <see cref="AssistantSettingsNeedPasswordQuery"/> from the pure rule of the domain.</summary>
internal sealed class AssistantSettingsNeedPasswordHandler(IAssistantSettingsStore store) : IQueryHandler<AssistantSettingsNeedPasswordQuery, bool>
{
    public async ValueTask<bool> HandleAsync(AssistantSettingsNeedPasswordQuery query, CancellationToken cancellationToken)
    {
        var current = await store.CurrentAsync(cancellationToken);
        return AssistantSettingsChange.RaisesExposure(
            current, query.Enabled, query.PortfolioContext, query.MaxToolCalls,
            query.AllowDraftConfirm ?? current.AllowDraftConfirm, query.AutoLabQuickTest ?? current.AutoLabQuickTest, query.AllowProposalAccept ?? current.AllowProposalAccept, query.AutoAcceptHypothesis ?? current.AutoAcceptHypothesis);
    }
}
