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
/// The readiness reads for the environment live (ADR 0171 step 6, ADR 0172), per account, composed only in live with <c>Execution:ReadinessLiveEnabled</c> (default OFF). The token comes only from the sealed same-day hand-over (<see cref="ILiveTokenSource"/>), never
/// from <see cref="IExecCredentials"/>, and this class is not an order client: it holds the shared GET machinery (<see cref="DhanReader"/>) only, whose live addresses are the two GETs of the guard table (<c>GET /v2/profile</c>, <c>GET /v2/ip/getIP</c> on
/// <see cref="ExecutionEndpoints.LiveHost"/>). Its own HTTP client goes through the host-pinned forward proxy <c>xd-exec-proxy</c> (from <c>HTTPS_PROXY</c>, set only by <c>deploy/compose.exec-live.yaml</c>) with the <see cref="GuardHandler"/> in front of
/// the socket; its own read limit is 5 requests a second (a retry included). With the switch off: <c>token_unavailable</c>, no token lookup. Without a proxy: <c>no_live_route</c>, no token lookup. Without a token: <c>token_unavailable</c>. None of those makes a
/// request. 401 or DH-901 is <c>token_invalid</c> at once; every request and refusal writes the audit log line (event 6616) without a token, host or address.
/// </summary>
internal sealed class LiveBrokerFacts : IBrokerFacts
{
    public const string TokenUnavailable = "token_unavailable";

    public const string NoLiveRoute = "no_live_route";

    public const int ReadsPerSecond = 5;

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly ILiveTokenSource _tokens;
    private readonly bool _enabled;
    private readonly DhanReader? _reader;

    // shortcut: as DhanBrokerFacts, a 24 h list of admits; fine at readiness poll rates.
    private readonly ExecRateLimiter _reads;

    // socket: the handler under the guard (ProxySocket in the host, a stub in tests); null (no proxy configured) means no client exists and every read is no_live_route.
    public LiveBrokerFacts(ILiveTokenSource tokens, bool enabled, HttpMessageHandler? socket, ILogger<LiveBrokerFacts> logger, TimeProvider clock)
    {
        _tokens = tokens;
        _enabled = enabled;
        _reads = new ExecRateLimiter(clock, ReadsPerSecond, int.MaxValue, int.MaxValue);
        _reader = socket is null ? null : new DhanReader(new HttpClient(new GuardHandler(ExecEnvironment.Live, socket)) { Timeout = Timeout.InfiniteTimeSpan }, ExecEnvironment.Live, logger, clock, ReadTimeout);
    }

    /// <summary>The socket of the live reads: through the given forward proxy only (CONNECT, TLS passes through), no redirects, no cookies, no decompression, a 5 s connect timeout. Null when no proxy is configured.</summary>
    public static SocketsHttpHandler? ProxySocket(Uri? proxy) => proxy is null
        ? null
        : new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = true,
            Proxy = new System.Net.WebProxy(proxy) { BypassProxyOnLocal = false },
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };

    public Task<BrokerRead<ProfileFacts>> GetProfileAsync(Guid accountId, CancellationToken cancellationToken) => ReadAsync(accountId, DhanCall.ProfileGet, DhanReads.Profile, cancellationToken);

    public Task<BrokerRead<IpFacts>> GetStaticIpAsync(Guid accountId, CancellationToken cancellationToken) => ReadAsync(accountId, DhanCall.StaticIpGet, DhanReads.StaticIp, cancellationToken);

    private async Task<BrokerRead<T>> ReadAsync<T>(Guid accountId, DhanCall call, Func<string, BrokerRead<T>> parse, CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return BrokerRead.Unreadable<T>(TokenUnavailable);
        }

        if (_reader is null)
        {
            return BrokerRead.Unreadable<T>(NoLiveRoute);
        }

        var token = await _tokens.FindAsync(accountId, cancellationToken).ConfigureAwait(false);
        return token is null
            ? _reader.Refuse<T>(_reads.TryAdmit, call, TokenUnavailable)
            : await _reader.ReadAsync(call, token.Reveal(), parse, cancellationToken, _reads.TryAdmit).ConfigureAwait(false);
    }
}
