using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Abstractions.Platform;

/// <summary>What the live feed reports about itself every 15 seconds (ADR 0024, P2-10 criterion 15): stored as the details of its row in <c>platform.service_heartbeats</c>. Counts and times only - never a token, a client id or a packet. <see cref="PacketsByCode"/> (additive) counts the messages of the window by response code: decoded, dropped with the reasons, and the first and latest message length - only codes actually seen, the busiest few.</summary>
public sealed record FeedHeartbeat(
    bool Connected,
    int Subscribed,
    DateTimeOffset? LastTickAt,
    double TicksPerSecond,
    int ReconnectsToday,
    long DroppedPackets,
    int FlushMs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, PacketCodeSummary>? PacketsByCode = null);

/// <summary>The feed's heartbeat as read back: its state word (<see cref="State"/>), when it was last seen, whether that is recent, and its figures (null when the row has none or they cannot be read).</summary>
public sealed record FeedStatus(string State, DateTimeOffset LastSeenAt, bool IsAlive, FeedHeartbeat? Details);

/// <summary>Writes and reads the feed's heartbeat. Uses the existing heartbeat store: the row of the service <see cref="ServiceName"/>.</summary>
public interface IFeedHeartbeats
{
    /// <summary>The service name of the feed in <c>platform.service_heartbeats</c> (never rename it).</summary>
    public const string ServiceName = "xd-feed";

    /// <summary>Records that the feed is alive with its state word (for example <c>connected</c>, <c>idle</c>, <c>closed</c>, <c>token_refused</c>) and figures.</summary>
    ValueTask ReportAsync(string state, FeedHeartbeat details, string instance, string version, CancellationToken cancellationToken);

    /// <summary>The feed's last heartbeat; null when it has never reported.</summary>
    ValueTask<FeedStatus?> GetAsync(CancellationToken cancellationToken);
}
