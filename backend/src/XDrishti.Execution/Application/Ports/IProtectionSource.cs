using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>One open position as the engine and the broker's book last agreed (P12-04). <see cref="NetQty"/> is from our fills; <see cref="BrokerNetQty"/> and its time are the last broker position read, null when none.</summary>
public sealed record ProtectedPosition(
    Guid AccountId,
    long IntentId,
    string SecurityId,
    ExecState State,
    int NetQty,
    decimal IntentStop,
    decimal IntentEntry,
    decimal? Last,
    DateTimeOffset? RaisedAt,
    int? BrokerNetQty,
    DateTimeOffset? BrokerPositionAt);

/// <summary>An entry order still working (or part-filled) at the broker.</summary>
public sealed record WorkingEntry(Guid AccountId, long IntentId, string ClientOrderId);

/// <summary>
/// Everything the protection logic judges, as of <see cref="BookReadAt"/> (the time the broker's order book was last read by stream or poll; null when never). <see cref="Halt"/> is the halt level in force, null when none. A source that cannot say
/// returns null instead of a snapshot, never an empty one.
/// </summary>
public sealed record ProtectionSnapshot(
    DateTimeOffset? BookReadAt,
    IReadOnlyList<BrokerOrderView> OrderBook,
    IReadOnlyList<ProtectedPosition> Positions,
    IReadOnlyList<WorkingEntry> Entries,
    HaltLevel? Halt,
    DateOnly SessionDate);

/// <summary>Reads the snapshot. The default answers null (unknown), which is never read as protected: the exit policy refuses and the watchdog alarms. P12-06 supplies the real one from the stream, the poll and the stored fills.</summary>
public interface IProtectionSource
{
    Task<ProtectionSnapshot?> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class ClosedProtectionSource : IProtectionSource
{
    public Task<ProtectionSnapshot?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<ProtectionSnapshot?>(null);
}
