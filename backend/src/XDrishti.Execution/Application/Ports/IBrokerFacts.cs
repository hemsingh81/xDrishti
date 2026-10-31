using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Application.Ports;

/// <summary>
/// The two broker reads readiness needs and nothing else (spec P12-11 criterion 13): the profile (<c>GET /v2/profile</c>) and the configured static IP (<c>GET /v2/ip/getIP</c>) of one account (ADR 0171: account-scoped). The adapter reads the token only through its
/// token source; no order, trade, position or funds call is reachable through this port. The sandbox adapter (<c>DhanBrokerFacts</c>, ADR 0170) is composed only with the sandbox send composition and answers with the sandbox credentials for every account; the live
/// reads (<c>LiveBrokerFacts</c>, ADR 0171) only in live with <c>Execution:ReadinessLiveEnabled</c>; everywhere else <see cref="ClosedBrokerFacts"/> answers unreadable, which makes readiness unavailable.
/// </summary>
public interface IBrokerFacts
{
    Task<BrokerRead<ProfileFacts>> GetProfileAsync(Guid accountId, CancellationToken cancellationToken);

    Task<BrokerRead<IpFacts>> GetStaticIpAsync(Guid accountId, CancellationToken cancellationToken);
}

internal sealed class ClosedBrokerFacts : IBrokerFacts
{
    public Task<BrokerRead<ProfileFacts>> GetProfileAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(BrokerRead.Unreadable<ProfileFacts>("no_broker"));

    public Task<BrokerRead<IpFacts>> GetStaticIpAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(BrokerRead.Unreadable<IpFacts>("no_broker"));
}
