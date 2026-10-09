namespace XDrishti.Application.Abstractions.Platform;

/// <summary>Reads connectivity and version information from the database.</summary>
public interface IDatabaseInfoProvider
{
    ValueTask<DatabaseInfo> GetAsync(CancellationToken cancellationToken);
}

/// <param name="IsConnected">Whether a round trip to the database succeeded.</param>
/// <param name="ServerVersion">PostgreSQL version, e.g. "18.6".</param>
/// <param name="TimescaleVersion">TimescaleDB extension version, or null when not installed.</param>
/// <param name="LatestMigration">Most recent applied schema migration.</param>
/// <param name="PendingMigrations">Migrations known to the code but not yet applied.</param>
/// <param name="LatencyMs">Round-trip time of the check.</param>
/// <param name="Error">Short reason when not connected.</param>
public sealed record DatabaseInfo(
    bool IsConnected,
    string? ServerVersion,
    string? TimescaleVersion,
    string? LatestMigration,
    int PendingMigrations,
    double LatencyMs,
    string? Error);
