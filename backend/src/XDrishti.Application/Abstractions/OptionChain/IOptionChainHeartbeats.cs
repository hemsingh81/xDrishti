namespace XDrishti.Application.Abstractions.OptionChain;

/// <summary>What the option-chain recorder reports about itself every 15 seconds (the details of its own row <c>xd-options</c> in <c>platform.service_heartbeats</c>). Counts only - never a token, a client id, a symbol or a price.</summary>
public sealed record ChainHeartbeat(bool DeploymentEnabled, int Slots, int Requests, int Ok, int Gaps, long Rows, bool FreeSpaceReadable);

/// <summary>The recorder's heartbeat as read back: its state word, when it was last seen, whether that is recent and its figures (null when they cannot be read).</summary>
public sealed record ChainStatus(string State, DateTimeOffset LastSeenAt, bool IsAlive, ChainHeartbeat? Details);

/// <summary>Writes and reads the option-chain recorder's heartbeat (its own row).</summary>
public interface IOptionChainHeartbeats
{
    /// <summary>The service name of the row in <c>platform.service_heartbeats</c> (never rename it).</summary>
    public const string ServiceName = "xd-options";

    ValueTask ReportAsync(string state, ChainHeartbeat details, string instance, string version, CancellationToken cancellationToken);

    ValueTask<ChainStatus?> GetAsync(CancellationToken cancellationToken);
}
