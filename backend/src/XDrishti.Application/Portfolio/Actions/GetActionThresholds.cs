using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>The thresholds of the Action-center rules in force (the stored ones, or the defaults), with the defaults for "Restore defaults" (ADR 0022).</summary>
public sealed record GetActionThresholdsQuery : IQuery<ActionThresholdsView>;

internal sealed class GetActionThresholdsHandler(IActionSettings settings) : IQueryHandler<GetActionThresholdsQuery, ActionThresholdsView>
{
    public async ValueTask<ActionThresholdsView> HandleAsync(GetActionThresholdsQuery query, CancellationToken cancellationToken) =>
        ActionThresholdsWriter.ViewOf(await settings.GetAsync(cancellationToken));
}
