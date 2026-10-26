namespace XDrishti.Infrastructure.Capabilities;

/// <summary>A row of <c>platform.capability_changes</c> (spec P8-18 Part A, ADR 0128): one switch of a capability, insert-only. The id, the time, the previous id and the snapshot hash are drawn by a trigger under a lock.</summary>
internal sealed class CapabilityChangeRecord
{
    public long Id { get; set; }

    public string Capability { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; }

    public long? PreviousId { get; set; }

    public string SnapshotHash { get; set; } = string.Empty;
}
