using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.Platform.Heartbeats;

/// <summary>
/// The feed's heartbeat (ADR 0024): the existing heartbeat row of the service <see cref="IFeedHeartbeats.ServiceName"/> with its <c>state</c> and <c>details</c> filled. The worker's heartbeat and the System status list keep working unchanged.
/// A reader treats a row without details (or details it cannot read) as a feed that is alive but says nothing.
/// </summary>
internal sealed class FeedHeartbeats(IServiceHeartbeatStore store, TimeProvider clock) : IFeedHeartbeats
{
    /// <summary>The feed counts as down when its heartbeat is older than this (the heartbeat is every 15 s; the container health check uses the same 60 s).</summary>
    internal static readonly TimeSpan AliveTolerance = TimeSpan.FromSeconds(60);

    public async ValueTask ReportAsync(string state, FeedHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(details);
        var now = clock.GetUtcNow();
        var json = FeedJson.Write(details);
        var heartbeat = await store.FindAsync(IFeedHeartbeats.ServiceName, cancellationToken);
        if (heartbeat is null)
        {
            heartbeat = ServiceHeartbeat.Start(IFeedHeartbeats.ServiceName, instance, version, now);
            store.Add(heartbeat);
        }

        heartbeat.Beat(instance, version, now, state, json);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<FeedStatus?> GetAsync(CancellationToken cancellationToken)
    {
        var heartbeat = await store.FindAsync(IFeedHeartbeats.ServiceName, cancellationToken);
        return heartbeat is null
            ? null
            : new FeedStatus(heartbeat.State ?? "unknown", heartbeat.LastSeenAt, heartbeat.IsAlive(clock.GetUtcNow(), AliveTolerance), FeedJson.Read(heartbeat.Details));
    }
}
