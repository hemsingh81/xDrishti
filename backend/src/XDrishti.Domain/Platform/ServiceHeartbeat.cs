using XDrishti.Domain.Common;

namespace XDrishti.Domain.Platform;

/// <summary>
/// The latest "I am alive" signal of a background service (worker, feed, …). One row per service name.
/// </summary>
public sealed class ServiceHeartbeat : Entity<string>
{
    public const int MaxStateLength = 32;

    /// <summary>The state of the last beat of a service that was told to stop (a deploy, a shutdown): it is down on purpose, which is not the same as having crashed.</summary>
    public const string StoppedState = "stopped";

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

    /// <summary>What the service says about itself in one word (the feed: <c>connected</c>, <c>idle</c>, <c>closed</c>, …); null for services that only say they are alive.</summary>
    public string? State { get; private set; }

    /// <summary>The service's own figures as a JSON object (the feed: subscribed count, last tick time, ticks per second, …); null when it has none. Never a secret.</summary>
    public string? Details { get; private set; }

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

    /// <summary>Records a beat together with the service's state and details (replacing the earlier ones). An out-of-order beat is ignored like in <see cref="Beat(string, string, DateTimeOffset)"/>.</summary>
    public void Beat(string instance, string version, DateTimeOffset now, string state, string details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(details);
        if (now < LastSeenAt)
        {
            return;
        }

        Beat(instance, version, now);
        State = state.Length <= MaxStateLength ? state : state[..MaxStateLength];
        Details = details;
    }

    public bool IsAlive(DateTimeOffset now, TimeSpan tolerance) => now - LastSeenAt <= tolerance;

    /// <summary>Whether the service said, in its last beat, that it was stopped on purpose.</summary>
    public bool IsStoppedOnPurpose => State == StoppedState;
}
