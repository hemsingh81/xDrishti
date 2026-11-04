using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionPhaseCheck;

namespace XDrishti.Infrastructure.OptionPhaseCheck;

/// <summary>The read side of the spread model store alone (P14-17): the id of the newest model of an underlying. It calls <c>LatestAsync</c> and nothing else.</summary>
internal sealed class SpreadModelIds(ISpreadModelStore models) : ISpreadModelIds
{
    public async ValueTask<string?> LatestAsync(Guid underlyingId, CancellationToken cancellationToken) => (await models.LatestAsync(underlyingId, cancellationToken))?.ModelId;
}
