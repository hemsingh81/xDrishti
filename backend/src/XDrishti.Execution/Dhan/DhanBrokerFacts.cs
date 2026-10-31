using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Dhan;

/// <summary>
/// The readiness reads through the sandbox adapter (spec P12-11 criterion 13, ADR 0170): the profile and the configured static IP, nothing else. It holds the adapter privately, so a consumer of <see cref="IBrokerFacts"/> cannot reach an order, trade, position
/// or funds call by a cast. The token is read only by the adapter, through <see cref="IExecCredentials"/>; missing credentials refuse locally with no request. Its own read limiter (5 requests a second, a retry included) is apart from the send limiter.
/// </summary>
internal sealed class DhanBrokerFacts(DhanOrderClient client, TimeProvider clock) : IBrokerFacts
{
    public const int ReadsPerSecond = 5;

    // shortcut: the limiter keeps a 24 h list of admits (minute and day caps unbounded); fine at readiness poll rates, use a one-second-only window if reads grow.
    private readonly ExecRateLimiter _reads = new(clock, ReadsPerSecond, int.MaxValue, int.MaxValue);

    public Task<BrokerRead<ProfileFacts>> GetProfileAsync(CancellationToken cancellationToken) => client.GetProfileAsync(_reads.TryAdmit, cancellationToken);

    public Task<BrokerRead<IpFacts>> GetStaticIpAsync(CancellationToken cancellationToken) => client.GetStaticIpAsync(_reads.TryAdmit, cancellationToken);
}
