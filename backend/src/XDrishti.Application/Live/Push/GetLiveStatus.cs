using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Application.Live.Push;

/// <summary>The state of the live feed from its heartbeat (REST twin of the hub's <c>feedStatus</c>).</summary>
public sealed record GetLiveStatusQuery : IQuery<LiveFeedStatus>;

internal sealed class GetLiveStatusHandler(IFeedHeartbeats heartbeats) : IQueryHandler<GetLiveStatusQuery, LiveFeedStatus>
{
    public async ValueTask<LiveFeedStatus> HandleAsync(GetLiveStatusQuery query, CancellationToken cancellationToken) =>
        LiveStatusMapper.Map(await heartbeats.GetAsync(cancellationToken));
}

/// <summary>Reads the feed's heartbeat the way the Services screen does: a feed that stopped on purpose, or has not reported in 60 seconds, is <c>not_running</c>.</summary>
internal static class LiveStatusMapper
{
    public static LiveFeedStatus Map(FeedStatus? heartbeat)
    {
        var reporting = heartbeat is { IsAlive: true } && heartbeat.State != FeedStates.Stopped;
        var lastTick = heartbeat?.Details?.LastTickAt;
        return reporting
            ? new LiveFeedStatus(heartbeat!.State, Math.Max(0, heartbeat.Details?.Subscribed ?? 0), lastTick)
            : new LiveFeedStatus(LiveFeedHealth.NotReporting, 0, lastTick);
    }
}
