using System.Collections.Concurrent;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>How many times a claimed registration's run failed for a reason that may pass (a database, timeout or I/O failure), kept in memory by the host. At <see cref="Max"/> the run is stored <c>run_failed</c>; a restart starts the count again, which only gives the run more tries.</summary>
public sealed class HoldOutRunAttempts
{
    /// <summary>The most transient failures a run is given before it is stored <c>run_failed</c>.</summary>
    public const int Max = 5;

    private readonly ConcurrentDictionary<Guid, int> _failures = new();

    /// <summary>Counts one more failure of the registration and returns how many there are.</summary>
    public int Add(Guid registrationId) => _failures.AddOrUpdate(registrationId, 1, (_, n) => n + 1);

    /// <summary>Forgets the registration (its result is stored).</summary>
    public void Clear(Guid registrationId) => _failures.TryRemove(registrationId, out _);
}
