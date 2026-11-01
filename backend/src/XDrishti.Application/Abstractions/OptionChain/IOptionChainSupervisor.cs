namespace XDrishti.Application.Abstractions.OptionChain;

/// <summary>The option-chain recorder's whole life in one call, for the host (P14-02): the three gates, the window, the run record, the requests, the disk guard and the heartbeat.</summary>
public interface IOptionChainSupervisor
{
    /// <summary>Runs until cancelled. Ends the open run before it returns.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
