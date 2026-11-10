namespace XDrishti.Application.Abstractions.SimAll;

/// <summary>Whether the bar history backfill is complete (spec AU-12, ADR 0243 point 7): the job refuses to start, and waits, while it is not.</summary>
public interface ISimAllBackfillGate
{
    /// <summary>Null when the backfill is complete; otherwise the reason, in words fit for the owner.</summary>
    ValueTask<string?> IncompleteReasonAsync(CancellationToken cancellationToken);

    /// <summary>True while a bar history job runs (it uses the broker); the simulation then waits.</summary>
    ValueTask<bool> DataJobRunningAsync(CancellationToken cancellationToken);
}

/// <summary>The instruments a job covers: tracked, active and with stored history.</summary>
public interface ISimAllUniverse
{
    /// <summary>The ids in a fixed order (by id), so the chunk order is the same on every start.</summary>
    ValueTask<IReadOnlyList<Guid>> InstrumentIdsAsync(CancellationToken cancellationToken);
}

/// <summary>Free space of the volume the database lives on.</summary>
public interface IFreeDiskProbe
{
    /// <summary>Free bytes, or null when they cannot be read (then the job waits: unknown is not enough).</summary>
    long? FreeBytes();
}
