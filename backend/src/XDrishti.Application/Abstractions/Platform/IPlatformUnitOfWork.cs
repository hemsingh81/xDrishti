namespace XDrishti.Application.Abstractions.Platform;

/// <summary>Saves what the schedule store and the audit log have collected, atomically (the scoped database context collects the changes of every store of the request).</summary>
public interface IPlatformUnitOfWork
{
    /// <summary>
    /// Saves everything collected; false (and the collected changes discarded) when the database refused it because a row had been changed since it was read (the revision of a service's
    /// schedule) or a row with the same key was added meanwhile (the first save of a service, twice at once).
    /// </summary>
    ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken);
}
