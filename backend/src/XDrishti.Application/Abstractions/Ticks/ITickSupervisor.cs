namespace XDrishti.Application.Abstractions.Ticks;

/// <summary>The tick recorder's whole life in one call, for the host (P7-16b): the three gates, the window, the run record, the connection, the disk guard and the heartbeat.</summary>
public interface ITickSupervisor
{
    /// <summary>Runs until cancelled. Ends the open run and stops the connection cleanly before it returns.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
