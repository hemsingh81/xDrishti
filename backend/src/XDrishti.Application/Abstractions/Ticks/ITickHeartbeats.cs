namespace XDrishti.Application.Abstractions.Ticks;

/// <summary>What the tick recorder reports about itself every 15 seconds (the details of its own row <c>xd-ticks</c> in <c>platform.service_heartbeats</c>). Counts only - never a token, a client id, a symbol or a price.</summary>
public sealed record TickHeartbeat(bool DeploymentEnabled, int Instruments, long Packets, long Rows, long Unchanged, long DroppedRows, int GapSeconds, int Reconnects, bool FreeSpaceReadable);

/// <summary>The tick recorder's heartbeat as read back: its state word, when it was last seen, whether that is recent and its figures (null when they cannot be read).</summary>
public sealed record TickStatus(string State, DateTimeOffset LastSeenAt, bool IsAlive, TickHeartbeat? Details);

/// <summary>Writes and reads the tick recorder's heartbeat (its own row, so that neither the live feed's health check nor the depth recorder's reads it).</summary>
public interface ITickHeartbeats
{
    /// <summary>The service name of the row in <c>platform.service_heartbeats</c> (never rename it).</summary>
    public const string ServiceName = "xd-ticks";

    ValueTask ReportAsync(string state, TickHeartbeat details, string instance, string version, CancellationToken cancellationToken);

    ValueTask<TickStatus?> GetAsync(CancellationToken cancellationToken);
}
