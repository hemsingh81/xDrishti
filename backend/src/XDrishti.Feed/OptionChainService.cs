using XDrishti.Application.Abstractions.OptionChain;

namespace XDrishti.Feed;

/// <summary>The fourth background loop of the host: hands the whole life of the option-chain recorder to its supervisor and stops it with the host. Its own loop and its own try/catch: it can neither stall nor fail the live feed or the other recorders.</summary>
internal sealed class OptionChainService(IOptionChainSupervisor supervisor) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => supervisor.RunAsync(stoppingToken);
}
