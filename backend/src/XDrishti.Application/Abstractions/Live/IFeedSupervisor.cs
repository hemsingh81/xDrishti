namespace XDrishti.Application.Abstractions.Live;

/// <summary>The feed's whole life in one call, for the host (ADR 0024): owns the window, the run record, the connection and the heartbeat. Hosts do nothing but start it.</summary>
public interface IFeedSupervisor
{
    /// <summary>Runs until cancelled. Ends the open run and stops the connection cleanly before it returns.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
