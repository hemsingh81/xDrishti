namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>The expired-options backfill's whole life in one call, for the host (P14-03): the four gates, the night window, the run record, the fetches, the disk guard and the heartbeat.</summary>
public interface IOptionBackfillSupervisor
{
    /// <summary>Runs until cancelled. Ends the open run before it returns.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
