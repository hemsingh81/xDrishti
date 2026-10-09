using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Application.Platform.SystemStatus;

/// <summary>Overall health of the running system: this API, the database and the background services.</summary>
public sealed record GetSystemStatusQuery : IQuery<SystemStatusResponse>;

public sealed record SystemStatusResponse(
    ApplicationStatus Application,
    DatabaseStatus Database,
    IReadOnlyList<ServiceStatus> Services,
    bool IsHealthy,
    DateTimeOffset CheckedAt);

public sealed record ApplicationStatus(string Name, string Version, string Environment, string Instance, DateTimeOffset StartedAt, long UptimeSeconds);

public sealed record DatabaseStatus(bool IsConnected, string? ServerVersion, string? TimescaleVersion, string? LatestMigration, int PendingMigrations, double LatencyMs, string? Error);

public sealed record ServiceStatus(string Name, string Instance, string Version, DateTimeOffset StartedAt, DateTimeOffset LastSeenAt, bool IsAlive);

internal sealed class GetSystemStatusHandler(
    IApplicationInfo application,
    IDatabaseInfoProvider database,
    IServiceHeartbeatStore heartbeats,
    TimeProvider clock) : IQueryHandler<GetSystemStatusQuery, SystemStatusResponse>
{
    /// <summary>A service that has not reported within this window is considered down.</summary>
    internal static readonly TimeSpan AliveTolerance = TimeSpan.FromSeconds(60);

    public async ValueTask<SystemStatusResponse> HandleAsync(GetSystemStatusQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var db = await database.GetAsync(cancellationToken);

        IReadOnlyList<ServiceStatus> services = [];
        if (db.IsConnected)
        {
            var beats = await heartbeats.ListAsync(cancellationToken);
            services = [.. beats.Select(b => new ServiceStatus(b.ServiceName, b.Instance, b.Version, b.StartedAt, b.LastSeenAt, b.IsAlive(now, AliveTolerance)))];
        }

        var app = new ApplicationStatus(
            application.Name,
            application.Version,
            application.Environment,
            application.Instance,
            application.StartedAt,
            (long)(now - application.StartedAt).TotalSeconds);

        var dbStatus = new DatabaseStatus(db.IsConnected, db.ServerVersion, db.TimescaleVersion, db.LatestMigration, db.PendingMigrations, db.LatencyMs, db.Error);
        var healthy = db.IsConnected && db.PendingMigrations == 0 && services.All(s => s.IsAlive);

        return new SystemStatusResponse(app, dbStatus, services, healthy, now);
    }
}
