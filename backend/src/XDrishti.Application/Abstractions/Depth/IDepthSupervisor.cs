namespace XDrishti.Application.Abstractions.Depth;

/// <summary>The depth recorder's whole life in one call, for the host (P7-16): the three gates, the window, the run record, the connection, the disk guard and the heartbeat. Hosts do nothing but start it.</summary>
public interface IDepthSupervisor
{
    /// <summary>Runs until cancelled. Ends the open run and stops the connection cleanly before it returns.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
