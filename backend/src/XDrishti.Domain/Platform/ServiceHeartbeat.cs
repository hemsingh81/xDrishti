using XDrishti.Domain.Common;

namespace XDrishti.Domain.Platform;

/// <summary>
/// The latest "I am alive" signal of a background service (worker, feed, …). One row per service name.
/// </summary>
public sealed class ServiceHeartbeat : Entity<string>
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private ServiceHeartbeat(string id, string instance, string version, DateTimeOffset startedAt)
        : base(id)
    {
        Instance = instance;
        Version = version;
        StartedAt = startedAt;
        LastSeenAt = startedAt;
    }

    public string ServiceName => Id;

    public string Instance { get; private set; }

    public string Version { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public static ServiceHeartbeat Start(string serviceName, string instance, string version, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return new ServiceHeartbeat(serviceName, instance, version, now);
    }

    /// <summary>Records a beat. A new instance or version means the service restarted.</summary>
    public void Beat(string instance, string version, DateTimeOffset now)
    {
        if (now < LastSeenAt)
        {
            return; // out-of-order beat; keep the latest
        }

        if (!string.Equals(instance, Instance, StringComparison.Ordinal) || !string.Equals(version, Version, StringComparison.Ordinal))
        {
            Instance = instance;
            Version = version;
            StartedAt = now;
        }

        LastSeenAt = now;
    }

    public bool IsAlive(DateTimeOffset now, TimeSpan tolerance) => now - LastSeenAt <= tolerance;
}
