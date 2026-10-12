using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market;

/// <summary>
/// An instrument the owner chose to work with (tracking, docs/design/06 §2.3). One row per instrument, kept for good: untracking only switches
/// <see cref="Active"/> off, so the coverage fields (<see cref="DataFrom"/>, <see cref="DataTo"/>, <see cref="LastUpdate"/>) survive and tracking again
/// continues where it stopped. Whether the instrument is still in Dhan's list is not stored here: it follows the master list.
/// </summary>
public sealed class TrackedInstrument : Entity<Guid>
{
    public const int MaxNoteLength = 500;

    public const int MaxTrackedByLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private TrackedInstrument(
        Guid id,
        bool active,
        DateTimeOffset activeSince,
        DateTimeOffset? deactivatedAt,
        DateOnly? dataFrom,
        DateOnly? dataTo,
        DateTimeOffset? lastUpdate,
        DateTimeOffset? backfillRequestedAt,
        string? note,
        string trackedBy)
        : base(id)
    {
        Active = active;
        ActiveSince = activeSince;
        DeactivatedAt = deactivatedAt;
        DataFrom = dataFrom;
        DataTo = dataTo;
        LastUpdate = lastUpdate;
        BackfillRequestedAt = backfillRequestedAt;
        Note = note;
        TrackedBy = trackedBy;
    }

    /// <summary>Tracked right now.</summary>
    public bool Active { get; private set; }

    /// <summary>When tracking last (re)started.</summary>
    public DateTimeOffset ActiveSince { get; private set; }

    /// <summary>When tracking was switched off; null while <see cref="Active"/>.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>First day with collected history; null until history exists (set by the history service, never cleared).</summary>
    public DateOnly? DataFrom { get; private set; }

    /// <summary>Last day with collected history.</summary>
    public DateOnly? DataTo { get; private set; }

    /// <summary>When history was last collected.</summary>
    public DateTimeOffset? LastUpdate { get; private set; }

    /// <summary>When history collection was last asked for (set each time tracking starts); the collecting job clears nothing here.</summary>
    public DateTimeOffset? BackfillRequestedAt { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Who tracked it (the signed-in owner's e-mail address).</summary>
    public string TrackedBy { get; private set; }

    /// <summary>Starts tracking an instrument that has never been tracked.</summary>
    public static TrackedInstrument Track(Guid instrumentId, string actor, DateTimeOffset now) =>
        new(instrumentId, true, now, null, null, null, null, now, null, Limit(actor));

    /// <summary>Tracks again after an untrack; keeps the history fields. False when it is tracked already (nothing changes).</summary>
    public bool Reactivate(string actor, DateTimeOffset now)
    {
        if (Active)
        {
            return false;
        }

        Active = true;
        ActiveSince = now;
        DeactivatedAt = null;
        BackfillRequestedAt = now;
        TrackedBy = Limit(actor);
        return true;
    }

    /// <summary>Stops tracking; keeps the history fields. False when it is not tracked (nothing changes).</summary>
    public bool Deactivate(DateTimeOffset now)
    {
        if (!Active)
        {
            return false;
        }

        Active = false;
        DeactivatedAt = now;
        return true;
    }

    private static string Limit(string actor)
    {
        var trimmed = actor?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "unknown" : trimmed.Length <= MaxTrackedByLength ? trimmed : trimmed[..MaxTrackedByLength];
    }
}
