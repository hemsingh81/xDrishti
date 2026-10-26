namespace XDrishti.Application.Abstractions.Depth;

/// <summary>What the depth recorder reports about itself every 15 seconds (stored as the details of its own row <c>xd-depth</c> in <c>platform.service_heartbeats</c>). Counts only - never a token, a client id, a symbol or a price.</summary>
public sealed record DepthHeartbeat(bool DeploymentEnabled, int Instruments, long Snapshots, long Packets, long DroppedRows, int GapSeconds, int Reconnects, bool FreeSpaceReadable);

/// <summary>The depth recorder's heartbeat as read back: its state word, when it was last seen, whether that is recent and its figures (null when they cannot be read).</summary>
public sealed record DepthStatus(string State, DateTimeOffset LastSeenAt, bool IsAlive, DepthHeartbeat? Details);

/// <summary>Writes and reads the depth recorder's heartbeat (its own row, so that the live feed's health check never reads it).</summary>
public interface IDepthHeartbeats
{
    /// <summary>The service name of the row in <c>platform.service_heartbeats</c> (never rename it).</summary>
    public const string ServiceName = "xd-depth";

    ValueTask ReportAsync(string state, DepthHeartbeat details, string instance, string version, CancellationToken cancellationToken);

    ValueTask<DepthStatus?> GetAsync(CancellationToken cancellationToken);
}
