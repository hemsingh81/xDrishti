using XDrishti.Domain.Capabilities;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Capabilities;

/// <summary>One stored switch of a capability.</summary>
/// <param name="Id">The row id (the newest id of a name decides).</param>
/// <param name="Name">The capability.</param>
/// <param name="Enabled">The state the switch set.</param>
/// <param name="Reason">Why.</param>
/// <param name="Actor">Who (an owner actor).</param>
/// <param name="ChangedAt">When (database clock).</param>
public sealed record CapabilityEntry(long Id, string Name, bool Enabled, string Reason, string Actor, DateTimeOffset ChangedAt);

/// <summary>The outcome of appending a switch.</summary>
/// <param name="Entry">The row written, or the newest row that already said the same; null when nothing was stored and the capability is already off.</param>
/// <param name="Created">True when a row was written; false when the log already said what the switch says.</param>
public sealed record CapabilityAppend(CapabilityEntry? Entry, bool Created);

/// <summary>
/// The read side of the capability log (<c>platform.capability_changes</c>, spec P8-18 Part A, ADR 0128). The newest row of a name decides; a name with no row is OFF. Reads only; the write side is <see cref="ICapabilityLogWriter"/>.
/// </summary>
public interface ICapabilityLog
{
    /// <summary>The newest row of each capability that has one (any order).</summary>
    ValueTask<IReadOnlyList<CapabilityEntry>> NewestAsync(CancellationToken cancellationToken);

    /// <summary>The latest rows of every capability, newest first.</summary>
    ValueTask<IReadOnlyList<CapabilityEntry>> HistoryAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// The write side (spec P8-18 Part A, ADR 0128): appends one row, never updates or deletes one. Under one lock it re-reads the newest row of the name and writes nothing when the log already says the same (two switches at once make one row). Only the owner's endpoint
/// reaches this port, through <c>ChangeCapabilityHandler</c> (an architecture test); no job, service, rule or other endpoint can switch a capability.
/// </summary>
public interface ICapabilityLogWriter
{
    /// <summary>Appends the switch (idempotent).</summary>
    ValueTask<Result<CapabilityAppend>> AppendAsync(CapabilityChange change, CancellationToken cancellationToken);
}

/// <summary>
/// The gate every capability-guarded write asks (spec P8-18 Part A, ADR 0128), generic by name: <c>RequireAsync("experiments_ui", ...)</c>. It READS the log; it has no way to switch anything. A name outside the whitelist, a name with no row and a newest row that says off
/// are all refused with <c>capabilities.off</c>.
/// </summary>
public interface ICapabilityGate
{
    /// <summary>Success when the capability is ON now; else the <c>capabilities.off</c> failure (403).</summary>
    ValueTask<Result> RequireAsync(string name, CancellationToken cancellationToken);

    /// <summary>True when the capability is ON now.</summary>
    ValueTask<bool> IsEnabledAsync(string name, CancellationToken cancellationToken);
}
