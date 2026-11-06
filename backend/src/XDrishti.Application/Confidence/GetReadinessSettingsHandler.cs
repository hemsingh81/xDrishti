using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Confidence;

/// <summary>The readiness settings now: the owner's switch (OFF when no row is stored) and the thresholds' text and hash. Read only.</summary>
internal sealed class GetReadinessSettingsHandler(IReadinessSettings settings, ReadinessOptionsView options) : IQueryHandler<GetReadinessSettingsQuery, ReadinessSettingsView>
{
    public async ValueTask<ReadinessSettingsView> HandleAsync(GetReadinessSettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return options.View(await settings.CurrentAsync(cancellationToken));
    }
}
