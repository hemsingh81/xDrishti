using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Ai;

/// <summary>Answers the question of <see cref="AssistantSettingsNeedPasswordQuery"/> from the pure rule of the domain.</summary>
internal sealed class AssistantSettingsNeedPasswordHandler(IAssistantSettingsStore store) : IQueryHandler<AssistantSettingsNeedPasswordQuery, bool>
{
    public async ValueTask<bool> HandleAsync(AssistantSettingsNeedPasswordQuery query, CancellationToken cancellationToken) =>
        AssistantSettingsChange.RaisesExposure(await store.CurrentAsync(cancellationToken), query.Enabled, query.PortfolioContext, query.MaxToolCalls);
}
