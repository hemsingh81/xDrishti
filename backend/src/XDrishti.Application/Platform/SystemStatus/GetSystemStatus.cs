using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform;

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

/// <summary>
/// A background process by its heartbeat. <c>isAlive</c>: it reported within the last 60 seconds. <c>state</c>: what it says about itself (the feed: connected, idle, closed, paused, …; <c>stopped</c> in the last beat of a process that was told
/// to stop on purpose, which does not make the system unhealthy for ten minutes); null for a process that only says it is alive (the worker).
/// </summary>
public sealed record ServiceStatus(string Name, string Instance, string Version, DateTimeOffset StartedAt, DateTimeOffset LastSeenAt, bool IsAlive, string? State = null);

internal sealed class GetSystemStatusHandler(
    IApplicationInfo application,
    IDatabaseInfoProvider database,
    IServiceHeartbeatStore heartbeats,
    TimeProvider clock) : IQueryHandler<GetSystemStatusQuery, SystemStatusResponse>
{
    /// <summary>A service that has not reported within this window is considered down.</summary>
    internal static readonly TimeSpan AliveTolerance = TimeSpan.FromSeconds(60);

    /// <summary>A process that said in its last beat that it was stopping on purpose is not a fault for this long; after that it is down like any process that stopped reporting.</summary>
    internal static readonly TimeSpan StoppedGrace = TimeSpan.FromMinutes(10);

    public async ValueTask<SystemStatusResponse> HandleAsync(GetSystemStatusQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var db = await database.GetAsync(cancellationToken);

        IReadOnlyList<ServiceStatus> services = [];
        if (db.IsConnected)
        {
            var beats = await heartbeats.ListAsync(cancellationToken);
            services = [.. beats.Select(b => new ServiceStatus(b.ServiceName, b.Instance, b.Version, b.StartedAt, b.LastSeenAt, b.IsAlive(now, AliveTolerance) && !b.IsStoppedOnPurpose, b.State))];
        }

        var app = new ApplicationStatus(
            application.Name,
            application.Version,
            application.Environment,
            application.Instance,
            application.StartedAt,
            (long)(now - application.StartedAt).TotalSeconds);

        var dbStatus = new DatabaseStatus(db.IsConnected, db.ServerVersion, db.TimescaleVersion, db.LatestMigration, db.PendingMigrations, db.LatencyMs, db.Error);
        // a process that said it was stopping on purpose (a deploy, xd-down) is down but not a fault for ten minutes; one that simply stopped reporting is, at once
        var healthy = db.IsConnected && db.PendingMigrations == 0
            && services.All(s => s.IsAlive || (s.State == ServiceHeartbeat.StoppedState && now - s.LastSeenAt <= StoppedGrace));

        return new SystemStatusResponse(app, dbStatus, services, healthy, now);
    }
}
