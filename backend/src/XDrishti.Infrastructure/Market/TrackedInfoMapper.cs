using XDrishti.Application.Market.Search;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.Market;

/// <summary>Builds the <c>tracked</c> block that every instrument in lists and details carries.</summary>
internal static class TrackedInfoMapper
{
    public static TrackedInfo From(TrackedInstrument? tracked, bool retired) =>
        tracked is null
            ? TrackedInfo.Never
            : new TrackedInfo(tracked.Active, tracked.ActiveSince, tracked.DeactivatedAt, tracked.DataFrom, tracked.DataTo, tracked.LastUpdate, tracked.BackfillRequestedAt, tracked.Active && retired);
}
