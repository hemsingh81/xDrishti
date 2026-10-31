using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Domain.Accounts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain.Readiness;
using XDrishti.Execution.Host;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Dhan;

/// <summary>
/// ADR 0171 step 6 and ADR 0172: the live readiness reads take the account's token only from the sealed hand-over, only with <c>Execution:ReadinessLiveEnabled</c> in live, and go out only through the host-pinned proxy, with the live guard (two GETs) in front
/// of the socket. Switch off, no proxy or no token: no request. A stub socket stands for the proxy (no network).
/// </summary>
public sealed class LiveBrokerFactsTests
{
    private const string Token = "FAKE-LIVE-TOKEN";

    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private const string ProfileSample = """
        {"dhanClientId":"1100003626","tokenValidity":"27/10/2026 17:00","activeSegment":"Equity"}
        """;

    private const string IpSample = """
        {"modifyDateSecondary":"2026-09-24","secondaryIP":"198.51.100.7","modifyDatePrimary":"2026-09-23","primaryIP":"203.0.113.10"}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Tokens(string? token) : ILiveTokenSource
    {
        public List<Guid> Asked { get; } = [];

        public Task<SecretValue?> FindAsync(Guid accountId, CancellationToken cancellationToken)
        {
            Asked.Add(accountId);
            return Task.FromResult(token is not null && SecretValue.TryCreate(token, out var value, out _) ? value : null);
        }
    }

    private sealed record Seen(HttpMethod Method, string Url, string? Token);

    private sealed class Script(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(request.Method, request.RequestUri!.ToString(), request.Headers.TryGetValues("access-token", out var v) ? v.Single() : null));
            return Task.FromResult(reply(request));
        }
    }

    private sealed class ListLogger : ILogger<LiveBrokerFacts>
    {
        public List<(EventId Id, string Line)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add((eventId, formatter(state, exception)));
    }

    private sealed record Rig(LiveBrokerFacts Facts, Script Socket, Tokens Tokens, FakeTimeProvider Clock, ListLogger Log);

    private static Rig Make(Func<HttpRequestMessage, HttpResponseMessage> reply, string? token = Token, bool enabled = true, bool proxy = true)
    {
        var socket = new Script(reply);
        var tokens = new Tokens(token);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 2, 30, 0, TimeSpan.Zero));
        var log = new ListLogger();
        return new Rig(new LiveBrokerFacts(tokens, enabled, proxy ? socket : null, log, clock), socket, tokens, clock, log);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };

    private static async Task<T> Drive<T>(Task<T> task, FakeTimeProvider clock)
    {
        while (!task.IsCompleted)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Yield();
        }

        return await task;
    }

    [Fact]
    public async Task With_the_switch_off_both_reads_are_token_unavailable_with_no_lookup_and_no_request()
    {
        var rig = Make(_ => Json(ProfileSample), enabled: false);

        (await rig.Facts.GetProfileAsync(Account, Ct)).Reason.ShouldBe("token_unavailable");
        (await rig.Facts.GetStaticIpAsync(Account, Ct)).Reason.ShouldBe("token_unavailable");
        rig.Tokens.Asked.ShouldBeEmpty();
        rig.Socket.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_proxy_both_reads_are_no_live_route_with_no_lookup_and_no_request()
    {
        var rig = Make(_ => Json(ProfileSample), proxy: false);

        (await rig.Facts.GetProfileAsync(Account, Ct)).Reason.ShouldBe("no_live_route");
        (await rig.Facts.GetStaticIpAsync(Account, Ct)).Reason.ShouldBe("no_live_route");
        rig.Tokens.Asked.ShouldBeEmpty();
        rig.Socket.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_hand_over_token_both_reads_are_token_unavailable_with_no_request()
    {
        var rig = Make(_ => Json(ProfileSample), token: null);

        var profile = await rig.Facts.GetProfileAsync(Account, Ct);
        var ip = await rig.Facts.GetStaticIpAsync(Account, Ct);

        (profile.Readable, profile.Reason, ip.Readable, ip.Reason).ShouldBe((false, "token_unavailable", false, "token_unavailable"));
        rig.Tokens.Asked.ShouldBe([Account, Account]);
        rig.Socket.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_a_token_each_read_is_one_guarded_get_on_the_live_host_with_the_token_in_the_header_only()
    {
        var rig = Make(r => Json(r.RequestUri!.AbsolutePath == "/v2/profile" ? ProfileSample : IpSample));

        var profile = await rig.Facts.GetProfileAsync(Account, Ct);
        var ip = await rig.Facts.GetStaticIpAsync(Account, Ct);

        profile.Value.ShouldBe(new ProfileFacts(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.FromHours(5.5))));
        ip.Value.ShouldBe(new IpFacts("203.0.113.10", "198.51.100.7", new DateOnly(2026, 9, 23), new DateOnly(2026, 9, 24)));
        rig.Socket.Requests.ShouldBe([new Seen(HttpMethod.Get, "https://api.dhan.co/v2/profile", Token), new Seen(HttpMethod.Get, "https://api.dhan.co/v2/ip/getIP", Token)]);
        rig.Tokens.Asked.ShouldBe([Account, Account]);
    }

    [Theory]
    [InlineData(401, "")]
    [InlineData(400, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Access token is expired"}""")]
    public async Task A_refused_token_is_token_invalid_at_once_with_no_second_request(int status, string body)
    {
        var rig = Make(_ => Json(body, (HttpStatusCode)status));

        var profile = await rig.Facts.GetProfileAsync(Account, Ct);

        (profile.Readable, profile.Reason).ShouldBe((false, "token_invalid"));
        rig.Socket.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_server_error_is_retried_once_then_read_failed()
    {
        var rig = Make(_ => Json("{}", HttpStatusCode.ServiceUnavailable));

        var ip = await Drive(rig.Facts.GetStaticIpAsync(Account, Ct), rig.Clock);

        (ip.Readable, ip.Reason).ShouldBe((false, "read_failed"));
        rig.Socket.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_is_a_failed_read()
    {
        var rig = Make(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.test/v2/profile") }, Content = new StringContent(string.Empty) });

        var profile = await Drive(rig.Facts.GetProfileAsync(Account, Ct), rig.Clock);

        (profile.Readable, profile.Reason).ShouldBe((false, "read_failed"));
        rig.Socket.Requests.ShouldAllBe(r => r.Url == "https://api.dhan.co/v2/profile");
    }

    [Fact]
    public async Task Reads_have_their_own_limit_of_five_requests_a_second_and_a_refused_read_makes_no_request()
    {
        var rig = Make(_ => Json(IpSample));

        for (var i = 0; i < LiveBrokerFacts.ReadsPerSecond; i++)
        {
            (await rig.Facts.GetStaticIpAsync(Account, Ct)).Readable.ShouldBeTrue();
        }

        var refused = await rig.Facts.GetProfileAsync(Account, Ct);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        var later = await rig.Facts.GetStaticIpAsync(Account, Ct);

        (refused.Readable, refused.Reason).ShouldBe((false, "read_rate_limited"));
        later.Readable.ShouldBeTrue();
        rig.Socket.Requests.Count.ShouldBe(LiveBrokerFacts.ReadsPerSecond + 1);
    }

    [Fact]
    public async Task Every_request_and_refusal_is_audited_without_a_token_host_or_address()
    {
        var rig = Make(r => Json(r.RequestUri!.AbsolutePath == "/v2/profile" ? ProfileSample : IpSample));
        var none = Make(_ => Json(ProfileSample), token: null);

        await rig.Facts.GetProfileAsync(Account, Ct);
        await rig.Facts.GetStaticIpAsync(Account, Ct);
        await none.Facts.GetStaticIpAsync(Account, Ct);

        rig.Log.Lines.Select(l => l.Line).ShouldBe(["Dhan live read ProfileGet GET /v2/profile answered 200: ok", "Dhan live read StaticIpGet GET /v2/ip/getIP answered 200: ok"]);
        none.Log.Lines.Select(l => l.Line).ShouldBe(["Dhan live read StaticIpGet GET /v2/ip/getIP answered 0: token_unavailable"]);
        foreach (var (id, line) in rig.Log.Lines.Concat(none.Log.Lines))
        {
            id.Id.ShouldBe(6616);
            line.ShouldNotContain(Token);
            line.ShouldNotContain("1100003626");
            line.ShouldNotContain("dhan.co");
            line.ShouldNotContain("xd-exec-proxy");
            line.ShouldNotContain("203.0.113.10");
            line.ShouldNotContain("198.51.100.7");
            line.ShouldNotContain(Account.ToString());
        }
    }

    [Fact]
    public void It_reaches_only_the_two_reads_and_is_no_credential_or_order_port()
    {
        typeof(LiveBrokerFacts).GetInterfaces().ShouldBe([typeof(IBrokerFacts)]);
        typeof(LiveBrokerFacts).GetMethods().Where(m => m.DeclaringType == typeof(LiveBrokerFacts)).Select(m => m.Name).Order().ShouldBe(["GetProfileAsync", "GetStaticIpAsync", "ProxySocket"]);
        typeof(LiveBrokerFacts).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Select(f => f.FieldType)
            .ShouldNotContain(t => t == typeof(DhanOrderClient) || t.IsAssignableTo(typeof(IExecCredentials)) || t.IsAssignableTo(typeof(IBrokerOrders)) || t.IsAssignableTo(typeof(IBrokerReads)) || t.IsAssignableTo(typeof(HttpMessageInvoker)));
        typeof(DhanReader).GetMethods().Where(m => m.DeclaringType == typeof(DhanReader)).Select(m => m.Name).Order().ShouldBe(["ReadAsync", "Refuse"]);
        typeof(ILiveTokenSource).IsAssignableTo(typeof(IExecCredentials)).ShouldBeFalse();
        typeof(PgExecTokenHandover).IsAssignableTo(typeof(IExecCredentials)).ShouldBeFalse();
    }

    [Fact]
    public void The_order_client_is_still_never_constructible_for_live()
    {
        using var http = new HttpClient();

        Should.Throw<InvalidOperationException>(() => new DhanOrderClient(
            http, new NoCredentials(), XDrishti.Execution.Domain.ExecEnvironment.Live, Microsoft.Extensions.Logging.Abstractions.NullLogger<DhanOrderClient>.Instance, TimeProvider.System, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
    }

    private sealed class NoCredentials : IExecCredentials
    {
        public DhanCredentials? Current() => null;
    }

    [Fact]
    public void The_proxy_socket_uses_the_given_proxy_and_follows_no_redirect_and_none_is_built_without_a_proxy()
    {
        LiveBrokerFacts.ProxySocket(null).ShouldBeNull();
        using var socket = LiveBrokerFacts.ProxySocket(new Uri("http://xd-exec-proxy:8888"))!;

        socket.UseProxy.ShouldBeTrue();
        socket.Proxy!.GetProxy(new Uri("https://api.dhan.co/v2/profile"))!.ToString().ShouldBe("http://xd-exec-proxy:8888/");
        socket.AllowAutoRedirect.ShouldBeFalse();
        socket.UseCookies.ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://xd-exec-proxy:8888", "http://xd-exec-proxy:8888/")]
    [InlineData("http://xd-exec-proxy:8888/", "http://xd-exec-proxy:8888/")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("xd-exec-proxy:8888", null)]
    [InlineData("https://xd-exec-proxy:8888", null)]
    [InlineData("socks5://xd-exec-proxy:1080", null)]
    [InlineData("http://user:pw@xd-exec-proxy:8888", null)]
    [InlineData("http://xd-exec-proxy:8888/path", null)]
    [InlineData("http://xd-exec-proxy:8888/?x=1", null)]
    public void The_proxy_setting_is_read_strictly(string? value, string? expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["HTTPS_PROXY"] = value }).Build();

        ExecOptions.LiveProxy(config)?.ToString().ShouldBe(expected);
    }

    private static IServiceCollection Compose(Dictionary<string, string?> values)
    {
        values["Database:ConnectionString"] = "Host=localhost;Port=1;Database=xdrishti;Username=xd_exec"; // never opened: no key, so no lookup reaches the database
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddLogging().AddExecution(config).AddExecutionRecord(config);
    }

    [Theory]
    [InlineData("live", null)]
    [InlineData("live", "false")]
    [InlineData("live", "yes")]
    [InlineData("none", "true")]
    [InlineData(null, "true")]
    [InlineData("sandbox", "true")]
    public void Outside_live_with_the_switch_on_no_live_reads_and_no_token_source_are_composed(string? environment, string? readinessLive)
    {
        var services = Compose(new() { ["Execution:Environment"] = environment, ["Execution:ReadinessLiveEnabled"] = readinessLive, ["HTTPS_PROXY"] = "http://xd-exec-proxy:8888" });

        services.Where(d => d.ServiceType == typeof(IBrokerFacts)).ShouldHaveSingleItem().ImplementationType.ShouldBe(typeof(ClosedBrokerFacts));
        services.ShouldNotContain(d => d.ServiceType == typeof(ILiveTokenSource));
    }

    [Fact]
    public async Task In_live_with_the_switch_on_and_no_proxy_the_live_reads_are_composed_and_answer_no_live_route()
    {
        var services = Compose(new() { ["Execution:Environment"] = "live", ["Execution:ReadinessLiveEnabled"] = "true", ["Execution:SendEnabled"] = "true" });

        await using var provider = services.BuildServiceProvider();
        var facts = provider.GetRequiredService<IBrokerFacts>();

        facts.ShouldBeOfType<LiveBrokerFacts>();
        (await facts.GetProfileAsync(Account, Ct)).Reason.ShouldBe("no_live_route");
        (await facts.GetStaticIpAsync(Account, Ct)).Reason.ShouldBe("no_live_route");
        services.ShouldNotContain(d => d.ServiceType == typeof(IExecCredentials));
        services.ShouldNotContain(d => d.ServiceType.Name == "LegSender");
    }

    [Fact]
    public async Task In_live_with_the_switch_and_the_proxy_on_but_without_the_key_the_reads_answer_token_unavailable()
    {
        var services = Compose(new() { ["Execution:Environment"] = "live", ["Execution:ReadinessLiveEnabled"] = "true", ["HTTPS_PROXY"] = "http://xd-exec-proxy:8888" });

        await using var provider = services.BuildServiceProvider();
        var facts = provider.GetRequiredService<IBrokerFacts>();

        facts.ShouldBeOfType<LiveBrokerFacts>();
        (await facts.GetProfileAsync(Account, Ct)).Reason.ShouldBe("token_unavailable");
        (await facts.GetStaticIpAsync(Account, Ct)).Reason.ShouldBe("token_unavailable");
        services.ShouldNotContain(d => d.ServiceType == typeof(IExecCredentials));
        services.ShouldNotContain(d => d.ServiceType == typeof(IBrokerReads));
        services.ShouldNotContain(d => d.ServiceType.Name == "LegSender");
    }
}
