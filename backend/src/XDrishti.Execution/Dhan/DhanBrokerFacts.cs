using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
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

    // The sandbox credentials are one secret file for the process: the same reads answer for every account (ADR 0170).
    public Task<BrokerRead<ProfileFacts>> GetProfileAsync(Guid accountId, CancellationToken cancellationToken) => client.GetProfileAsync(_reads.TryAdmit, cancellationToken);

    public Task<BrokerRead<IpFacts>> GetStaticIpAsync(Guid accountId, CancellationToken cancellationToken) => client.GetStaticIpAsync(_reads.TryAdmit, cancellationToken);
}

/// <summary>
/// The readiness reads for the environment live (ADR 0171 step 6), per account, composed only in live with <c>Execution:ReadinessLiveEnabled</c> (default OFF). The token comes only from the sealed same-day hand-over (<see cref="ILiveTokenSource"/>), never from
/// <see cref="IExecCredentials"/>. With the switch off or without a token the answer is <c>token_unavailable</c> and nothing else happens. With a token the read is put to the same guard table as every request: the table has no live row and <c>xd-exec</c> has no
/// route out (ADR 0171 step 5 is open), so the read is refused here, before any request exists. This class holds no HTTP client: live reads need the egress decision and live guard rows, both later slices.
/// </summary>
internal sealed class LiveBrokerFacts(ILiveTokenSource tokens, bool enabled) : IBrokerFacts
{
    public const string TokenUnavailable = "token_unavailable";

    public Task<BrokerRead<ProfileFacts>> GetProfileAsync(Guid accountId, CancellationToken cancellationToken) => ReadAsync<ProfileFacts>(accountId, DhanCall.ProfileGet, cancellationToken);

    public Task<BrokerRead<IpFacts>> GetStaticIpAsync(Guid accountId, CancellationToken cancellationToken) => ReadAsync<IpFacts>(accountId, DhanCall.StaticIpGet, cancellationToken);

    private async Task<BrokerRead<T>> ReadAsync<T>(Guid accountId, DhanCall call, CancellationToken cancellationToken)
    {
        if (!enabled || await tokens.FindAsync(accountId, cancellationToken).ConfigureAwait(false) is null)
        {
            return BrokerRead.Unreadable<T>(TokenUnavailable);
        }

        // The guard builds no live address (ExecutionEndpoints passes nothing for live). Should a later slice add live rows without the reads, the answer is still unreadable.
        return BrokerRead.Unreadable<T>(ExecutionEndpoints.Build(ExecEnvironment.Live, call) is null ? "guard_refused" : "no_live_route");
    }
}
