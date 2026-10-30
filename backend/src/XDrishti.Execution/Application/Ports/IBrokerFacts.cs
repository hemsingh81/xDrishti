using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Application.Ports;

/// <summary>
/// The two broker reads readiness needs and nothing else (spec P12-11 criterion 13): the profile (<c>GET /v2/profile</c>) and the configured static IP (<c>GET /v2/ip/getIP</c>). The adapter reads the token only through P12-02's token source; no order, trade, position or funds call is
/// reachable through this port. Until that adapter exists the only implementation answers unreadable, which makes readiness unavailable.
/// </summary>
public interface IBrokerFacts
{
    Task<BrokerRead<ProfileFacts>> GetProfileAsync(CancellationToken cancellationToken);

    Task<BrokerRead<IpFacts>> GetStaticIpAsync(CancellationToken cancellationToken);
}

internal sealed class ClosedBrokerFacts : IBrokerFacts
{
    public Task<BrokerRead<ProfileFacts>> GetProfileAsync(CancellationToken cancellationToken) => Task.FromResult(BrokerRead.Unreadable<ProfileFacts>("no_broker"));

    public Task<BrokerRead<IpFacts>> GetStaticIpAsync(CancellationToken cancellationToken) => Task.FromResult(BrokerRead.Unreadable<IpFacts>("no_broker"));
}
