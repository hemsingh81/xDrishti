using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Readiness;
using XDrishti.Execution.Host;

namespace XDrishti.Execution.Tests.Dhan;

/// <summary>
/// The readiness reads through the real sandbox client with a scripted handler (no network): spec P12-11 criterion 13, P12-02 criteria 1, 9, 10, 11; ADR 0170. The samples are the shapes of Dhan's profile and getIP pages, UNVERIFIED against the sandbox.
/// </summary>
public sealed class DhanBrokerFactsTests
{
    private const string Token = "SECRET-TOKEN-VALUE";
    private const string ClientId = "1100003626";

    private const string ProfileSample = """
        {"dhanClientId":"1100003626","tokenValidity":"27/10/2026 17:00","activeSegment":"Equity, Derivative, Currency, Commodity","ddpi":"Active","mtf":"Active","dataPlan":"Active","dataValidity":"2026-12-05 09:37:52.0"}
        """;

    private const string IpSample = """
        {"modifyDateSecondary":"2026-09-24","secondaryIP":"198.51.100.7","modifyDatePrimary":"2026-09-23","primaryIP":"203.0.113.10"}
        """;

    private sealed class Creds(bool present) : IExecCredentials
    {
        public DhanCredentials? Current() => present ? new DhanCredentials(ClientId, Token) : null;
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

    private sealed class ListLogger : ILogger<DhanOrderClient>
    {
        public List<(EventId Id, string Line)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add((eventId, formatter(state, exception)));
    }

    private sealed record Rig(DhanBrokerFacts Facts, Script Handler, FakeTimeProvider Clock, ListLogger Log);

    private static Rig Make(Func<HttpRequestMessage, HttpResponseMessage> reply, bool creds = true)
    {
        var handler = new Script(reply);
        var http = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, handler)) { Timeout = Timeout.InfiniteTimeSpan };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 3, 0, 0, TimeSpan.Zero));
        var log = new ListLogger();
        var client = new DhanOrderClient(http, new Creds(creds), ExecEnvironment.Sandbox, log, clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        return new Rig(new DhanBrokerFacts(client, clock), handler, clock, log);
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
    public async Task The_profile_sample_gives_the_token_validity_in_india_time_from_one_guarded_get()
    {
        var rig = Make(_ => Json(ProfileSample));

        var read = await rig.Facts.GetProfileAsync(CancellationToken.None);

        read.Readable.ShouldBeTrue();
        read.Value.ShouldBe(new ProfileFacts(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.FromHours(5.5))));
        rig.Handler.Requests.ShouldBe([new Seen(HttpMethod.Get, "https://sandbox.dhan.co/v2/profile", Token)]);
    }

    [Fact]
    public async Task The_static_ip_sample_gives_both_slots_and_their_change_dates_from_one_guarded_get()
    {
        var rig = Make(_ => Json(IpSample));

        var read = await rig.Facts.GetStaticIpAsync(CancellationToken.None);

        read.Readable.ShouldBeTrue();
        read.Value.ShouldBe(new IpFacts("203.0.113.10", "198.51.100.7", new DateOnly(2026, 9, 23), new DateOnly(2026, 9, 24)));
        rig.Handler.Requests.ShouldBe([new Seen(HttpMethod.Get, "https://sandbox.dhan.co/v2/ip/getIP", Token)]);
    }

    [Theory]
    [InlineData(401, "")]
    [InlineData(401, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Client ID or user generated access token is invalid or expired."}""")]
    [InlineData(400, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Access token is expired"}""")]
    [InlineData(403, """{"errorType":"Invalid_Authentication","errorCode":"dh-901","errorMessage":"expired"}""")]
    public async Task An_expired_or_refused_token_is_token_invalid_at_once_with_no_second_request(int status, string body)
    {
        var rig = Make(_ => Json(body, (HttpStatusCode)status));

        var profile = await rig.Facts.GetProfileAsync(CancellationToken.None);
        var ip = await rig.Facts.GetStaticIpAsync(CancellationToken.None);

        (profile.Readable, profile.Value, profile.Reason).ShouldBe((false, null, "token_invalid"));
        (ip.Readable, ip.Value, ip.Reason).ShouldBe((false, null, "token_invalid"));
        rig.Handler.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task A_server_error_is_retried_once_after_250_ms_then_read_failed(int status)
    {
        var rig = Make(_ => Json("""{"status":"error"}""", (HttpStatusCode)status));

        var profile = await Drive(rig.Facts.GetProfileAsync(CancellationToken.None), rig.Clock);
        var ip = await Drive(rig.Facts.GetStaticIpAsync(CancellationToken.None), rig.Clock);

        (profile.Readable, profile.Reason).ShouldBe((false, "read_failed"));
        (ip.Readable, ip.Reason).ShouldBe((false, "read_failed"));
        rig.Handler.Requests.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"ok\"")]
    [InlineData("{\"tokenValidity\":")]
    public async Task A_malformed_body_is_unreadable_never_a_default(string body)
    {
        var rig = Make(_ => Json(body));

        var profile = await rig.Facts.GetProfileAsync(CancellationToken.None);
        var ip = await rig.Facts.GetStaticIpAsync(CancellationToken.None);

        (profile.Readable, profile.Value, profile.Reason).ShouldBe((false, null, "profile_unreadable"));
        (ip.Readable, ip.Value, ip.Reason).ShouldBe((false, null, "ip_unreadable"));
    }

    [Fact]
    public async Task A_profile_without_a_readable_validity_is_read_but_says_no_validity()
    {
        var rig = Make(_ => Json("""{"dhanClientId":"1100003626","tokenValidity":"tomorrow"}"""));

        var profile = await rig.Facts.GetProfileAsync(CancellationToken.None);

        profile.Value.ShouldBe(new ProfileFacts(null));
    }

    [Fact]
    public async Task Missing_credentials_refuse_locally_and_make_no_request()
    {
        var rig = Make(_ => Json(ProfileSample), creds: false);

        var profile = await rig.Facts.GetProfileAsync(CancellationToken.None);
        var ip = await rig.Facts.GetStaticIpAsync(CancellationToken.None);

        (profile.Readable, profile.Reason).ShouldBe((false, "execution.token_unavailable"));
        (ip.Readable, ip.Reason).ShouldBe((false, "execution.token_unavailable"));
        rig.Handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reads_have_their_own_limit_of_five_requests_a_second_and_a_refused_read_makes_no_request()
    {
        var rig = Make(_ => Json(IpSample));

        for (var i = 0; i < DhanBrokerFacts.ReadsPerSecond; i++)
        {
            (await rig.Facts.GetStaticIpAsync(CancellationToken.None)).Readable.ShouldBeTrue();
        }

        var refused = await rig.Facts.GetProfileAsync(CancellationToken.None);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        var later = await rig.Facts.GetProfileAsync(CancellationToken.None);

        (refused.Readable, refused.Reason).ShouldBe((false, "read_rate_limited"));
        later.Readable.ShouldBeTrue();
        rig.Handler.Requests.Count.ShouldBe(DhanBrokerFacts.ReadsPerSecond + 1);
    }

    [Fact]
    public async Task Every_request_and_refusal_is_audited_with_the_path_template_and_never_a_token_client_id_host_or_address()
    {
        var replies = new Queue<HttpResponseMessage>([Json(ProfileSample), Json("{}", HttpStatusCode.ServiceUnavailable), Json(IpSample), Json("", HttpStatusCode.Unauthorized)]);
        var rig = Make(_ => replies.Dequeue());

        await rig.Facts.GetProfileAsync(CancellationToken.None);
        await Drive(rig.Facts.GetStaticIpAsync(CancellationToken.None), rig.Clock);
        await rig.Facts.GetProfileAsync(CancellationToken.None);
        var none = Make(_ => Json(ProfileSample), creds: false);
        await none.Facts.GetStaticIpAsync(CancellationToken.None);

        rig.Log.Lines.Select(l => l.Line).ShouldBe([
            "Dhan sandbox read ProfileGet GET /v2/profile answered 200: ok",
            "Dhan sandbox read StaticIpGet GET /v2/ip/getIP answered 503: read_failed",
            "Dhan sandbox read StaticIpGet GET /v2/ip/getIP answered 200: ok",
            "Dhan sandbox read ProfileGet GET /v2/profile answered 401: token_invalid",
        ]);
        none.Log.Lines.Select(l => l.Line).ShouldBe(["Dhan sandbox read StaticIpGet GET /v2/ip/getIP answered 0: execution.token_unavailable"]);
        foreach (var (id, line) in rig.Log.Lines.Concat(none.Log.Lines))
        {
            id.Id.ShouldBe(6616);
            line.ShouldNotContain(Token);
            line.ShouldNotContain(ClientId);
            line.ShouldNotContain("sandbox.dhan.co");
            line.ShouldNotContain("?");
            line.ShouldNotContain("203.0.113.10");
            line.ShouldNotContain("198.51.100.7");
        }
    }

    [Fact]
    public void The_port_reaches_only_the_two_reads_and_cannot_be_cast_to_an_order_or_read_port()
    {
        typeof(DhanBrokerFacts).GetInterfaces().ShouldBe([typeof(IBrokerFacts)]);
        typeof(DhanBrokerFacts).GetMethods().Where(m => m.DeclaringType == typeof(DhanBrokerFacts)).Select(m => m.Name).Order().ShouldBe(["GetProfileAsync", "GetStaticIpAsync"]);
        typeof(DhanBrokerFacts).IsAssignableTo(typeof(IBrokerOrders)).ShouldBeFalse();
        typeof(DhanBrokerFacts).IsAssignableTo(typeof(IBrokerReads)).ShouldBeFalse();
        typeof(DhanBrokerFacts).IsAssignableTo(typeof(IBrokerStops)).ShouldBeFalse();
    }

    private static IServiceCollection Compose(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddLogging().AddExecution(config).AddExecutionRecord(config);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("none", "true")]
    [InlineData("live", "true")]
    [InlineData("sandbox", null)]
    [InlineData("sandbox", "false")]
    public void Without_sandbox_and_the_send_switch_the_facts_stay_the_closed_default(string? environment, string? sendEnabled)
    {
        var services = Compose(new() { ["Execution:Environment"] = environment, ["Execution:SendEnabled"] = sendEnabled });

        services.Where(d => d.ServiceType == typeof(IBrokerFacts)).ShouldHaveSingleItem().ImplementationType.ShouldBe(typeof(ClosedBrokerFacts));
    }

    [Fact]
    public async Task With_sandbox_and_the_send_switch_the_facts_are_the_dhan_reads_and_without_secret_files_they_refuse_locally()
    {
        var services = Compose(new() { ["Execution:Environment"] = "sandbox", ["Execution:SendEnabled"] = "true" });

        services.Where(d => d.ServiceType == typeof(IBrokerFacts)).ShouldHaveSingleItem().ImplementationFactory.ShouldNotBeNull();
        await using var provider = services.BuildServiceProvider();
        var facts = provider.GetRequiredService<IBrokerFacts>();
        facts.ShouldBeOfType<DhanBrokerFacts>();
        (await facts.GetProfileAsync(CancellationToken.None)).Reason.ShouldBe("execution.token_unavailable");
        (await facts.GetStaticIpAsync(CancellationToken.None)).Reason.ShouldBe("execution.token_unavailable");
    }
}
