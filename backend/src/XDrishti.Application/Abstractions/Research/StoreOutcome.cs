namespace XDrishti.Application.Abstractions.Research;

/// <summary>What a store did: wrote the run (<c>Stored</c>) or found the identical run under its key (<c>AlreadyStored</c>, a no-op). <see cref="CopyMilliseconds"/> is the time of the trade copy, 0 for an already stored run.</summary>
public sealed record StoreOutcome(long Id, bool WasAlreadyStored, long CopyMilliseconds = 0)
{
    /// <summary>The run was written.</summary>
    public static StoreOutcome Stored(long id, long copyMilliseconds) => new(id, false, copyMilliseconds);

    /// <summary>The identical run was there.</summary>
    public static StoreOutcome AlreadyStored(long id) => new(id, true);
}
