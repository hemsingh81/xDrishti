namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>What the backfill reports about itself every 15 seconds (the details of its own row <c>xd-backfill</c> in <c>platform.service_heartbeats</c>). Counts only - never a token, a client id, a symbol or a price. <c>Progress</c> is null until the first look.</summary>
public sealed record BackfillHeartbeat(bool DeploymentEnabled, double RequestsPerSecond, int RequestsToday, int Ok, int Gaps, long Bars, bool FreeSpaceReadable, BackfillProgress? Progress);

/// <summary>The backfill's heartbeat as read back: its state word, when it was last seen, whether that is recent and its figures (null when they cannot be read).</summary>
public sealed record BackfillStatus(string State, DateTimeOffset LastSeenAt, bool IsAlive, BackfillHeartbeat? Details);

/// <summary>Writes and reads the backfill's heartbeat (its own row).</summary>
public interface IOptionBackfillHeartbeats
{
    /// <summary>The service name of the row in <c>platform.service_heartbeats</c> (never rename it).</summary>
    public const string ServiceName = "xd-backfill";

    ValueTask ReportAsync(string state, BackfillHeartbeat details, string instance, string version, CancellationToken cancellationToken);

    ValueTask<BackfillStatus?> GetAsync(CancellationToken cancellationToken);
}
