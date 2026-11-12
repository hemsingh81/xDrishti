using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Infrastructure.Ai.ReadOnly;
using XDrishti.Domain.Assistant;
using XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;
using XDrishti.Infrastructure.Ai.ReadOnly.EvidenceApi;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>
/// AI-01 AC 2 and 3 (ADR 0280): the evidence reader maps the five kinds to five GETs, hands the API's labels, states and the replay fence on unchanged, validates its arguments before any call, and fails closed (no address or token, 5xx, 401, a timeout, a body too large or not JSON). A fake
/// <see cref="HttpMessageHandler"/> stands in for the API.
/// </summary>
public sealed class EvidenceApiTests
{
    private const string Base = "http://xd-api:8080";
    private const string Run = "00000000-0000-7000-8000-0000000000a1";

    /// <summary>Made up for this run, never a real credential.</summary>
    private static readonly string Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public List<(string Method, string Url, string? Auth)> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString()));
            return reply(request);
        }
    }

    private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body = "{}") => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static (ApiEvidenceReader Reader, Stub Stub, CapturedLogger<EvidenceApiClient> Log) Make(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply, Action<BasketApiOptions>? configure = null)
    {
        var options = new BasketApiOptions { BaseUrl = Base, Token = Token };
        configure?.Invoke(options);
        var stub = new Stub(reply);
        var log = new CapturedLogger<EvidenceApiClient>();
        var guard = new EvidenceApiEndpointGuard(Options.Create(options)) { InnerHandler = stub };
        return (new ApiEvidenceReader(new EvidenceApiClient(new HttpClient(guard), Options.Create(options), TimeProvider.System, log)), stub, log);
    }

    private const string ReplayBody = """{"hindsight":[{"code":"filters_without_history","text":"Filters ran without their history."},{"code":"survivorship","text":"Only today's instruments."}],"id":"00000000-0000-7000-8000-0000000000a1","costsLabel":"before costs (charge sheet not filled, ISS-25)","figures":{"finalEquity":1012345.67}}""";

    [Fact]
    public async Task A_replay_answer_carries_every_label_verbatim_the_costs_label_and_the_fence()
    {
        var (reader, stub, _) = Make(_ => Reply(HttpStatusCode.OK, ReplayBody));

        var answer = await reader.GetEvidenceAsync("replay_run", Run, null, null, Ct);

        answer.ErrorCode.ShouldBeNull();
        var data = answer.Data!;
        data["fence"]!.GetValue<string>().ShouldBe(ApiEvidenceReader.ReplayFence);
        data["fence"]!.GetValue<string>().ShouldContain("research only, not evidence");
        var body = data["body"]!;
        body["hindsight"]!.AsArray().Select(l => l!["code"]!.GetValue<string>()).ShouldBe(["filters_without_history", "survivorship"]);
        body["hindsight"]![0]!["text"]!.GetValue<string>().ShouldBe("Filters ran without their history.");
        body["costsLabel"]!.GetValue<string>().ShouldContain("before costs");
        answer.Notes.ShouldContain(ApiEvidenceReader.ReplayFence);
        stub.Calls.ShouldBe([("GET", "/api/replay/runs/" + Run, "Bearer " + Token)]);
    }

    [Theory]
    [InlineData("replay_list", null, null, null, "/api/replay/runs?take=10")]
    [InlineData("readiness_list", null, null, null, "/api/confidence/scorecards")]
    [InlineData("readiness_card", null, "ORB", 3, "/api/confidence/scorecards/ORB/versions/3")]
    [InlineData("shadow_summary", null, null, null, "/api/paper/shadow/summary")]
    public async Task Each_kind_is_one_get_of_its_route(string kind, string? id, string? strategy, int? version, string expected)
    {
        var (reader, stub, _) = Make(_ => Reply(HttpStatusCode.OK, """{"state":"unavailable","reason":"no rows"}"""));

        var answer = await reader.GetEvidenceAsync(kind, id, strategy, version, Ct);

        answer.ErrorCode.ShouldBeNull();
        answer.Data!["body"]!["state"]!.GetValue<string>().ShouldBe("unavailable"); // passed through as text, never turned into a number
        stub.Calls.Select(c => c.Url).ShouldBe([expected]);
        (answer.Data["fence"] is null).ShouldBe(!kind.StartsWith("replay", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("replay_run", null, null, null)]
    [InlineData("replay_run", "not-a-guid", null, null)]
    [InlineData("readiness_card", null, "ORB", null)]
    [InlineData("readiness_card", null, null, 1)]
    [InlineData("readiness_card", null, "../x?y", 1)]
    [InlineData("nope", null, null, null)]
    public async Task A_bad_argument_is_refused_and_nothing_is_called(string kind, string? id, string? strategy, int? version)
    {
        var (reader, stub, _) = Make(_ => Reply(HttpStatusCode.OK));

        var answer = await reader.GetEvidenceAsync(kind, id, strategy, version, Ct);

        answer.ErrorCode.ShouldBe(ToolErrorCodes.InvalidArguments);
        stub.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "tok")]
    [InlineData("http://xd-api:8080", "")]
    public async Task No_address_or_no_token_is_unavailable_and_calls_nothing(string url, string token)
    {
        var (reader, stub, _) = Make(_ => Reply(HttpStatusCode.OK), o => (o.BaseUrl, o.Token) = (url, token));

        var answer = await reader.GetEvidenceAsync("shadow_summary", null, null, null, Ct);

        answer.ErrorCode.ShouldBe(ToolErrorCodes.Unavailable);
        answer.ErrorMessage.ShouldBe(EvidenceApiClient.UnavailableText);
        stub.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Any_other_status_is_unavailable_with_one_fixed_text(HttpStatusCode status)
    {
        var (reader, _, _) = Make(_ => Reply(status, "secret detail"));

        var answer = await reader.GetEvidenceAsync("replay_list", null, null, null, Ct);

        answer.ErrorCode.ShouldBe(ToolErrorCodes.Unavailable);
        answer.ErrorMessage.ShouldBe(EvidenceApiClient.UnavailableText);
        answer.Data.ShouldBeNull();
    }

    [Fact]
    public async Task A_404_is_not_found_and_a_422_is_invalid_arguments()
    {
        var (missing, _, _) = Make(_ => Reply(HttpStatusCode.NotFound));
        var (refused, _, _) = Make(_ => Reply(HttpStatusCode.UnprocessableEntity, """{"title":"x"}"""));

        (await missing.GetEvidenceAsync("replay_run", Run, null, null, Ct)).ErrorCode.ShouldBe(EvidenceApiClient.NotFoundCode);
        (await refused.GetEvidenceAsync("replay_list", null, null, null, Ct)).ErrorCode.ShouldBe(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task A_transport_error_a_timeout_an_oversized_body_and_a_body_that_is_not_json_are_unavailable()
    {
        var (down, _, _) = Make(_ => throw new HttpRequestException("connection refused by 10.0.0.5"));
        var (slow, _, _) = Make(_ => throw new TaskCanceledException("timeout"));
        var (big, _, _) = Make(_ => Reply(HttpStatusCode.OK, new string('x', 5000)), o => o.MaxResponseBytes = 1024);
        var (text, _, _) = Make(_ => Reply(HttpStatusCode.OK, "not json"));

        foreach (var reader in new[] { down, slow, big, text })
        {
            var answer = await reader.GetEvidenceAsync("shadow_summary", null, null, null, Ct);

            answer.ErrorCode.ShouldBe(ToolErrorCodes.Unavailable);
            answer.ErrorMessage!.ShouldNotContain("10.0.0.5");
        }
    }

    [Fact]
    public async Task The_token_and_the_request_are_never_logged()
    {
        var (reader, _, log) = Make(_ => Reply(HttpStatusCode.OK, ReplayBody));

        await reader.GetEvidenceAsync("replay_run", Run, null, null, Ct);

        log.Lines.ShouldNotBeEmpty();
        log.All().ShouldNotContain(Token);
        log.All().ShouldNotContain(Run);
    }

    [Fact]
    public async Task A_redirect_is_never_followed_by_either_client_so_the_token_cannot_leave_the_allow_list()
    {
        var hits = new List<string>();
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var peer = await listener.AcceptTcpClientAsync(stop.Token);
                    var stream = peer.GetStream();
                    var buffer = new byte[4096];
                    var read = await stream.ReadAsync(buffer, stop.Token);
                    lock (hits)
                    {
                        hits.Add(Encoding.ASCII.GetString(buffer, 0, read).Split(' ')[1]);
                    }

                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: /api/paper/shadow/summary\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, CancellationToken.None);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Api:BaseUrl"] = $"http://127.0.0.1:{port}", ["Mcp:Api:Token"] = Token }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddAiBasketApi(configuration);
        services.AddAiEvidenceApi();
        await using var provider = services.BuildServiceProvider();

        var evidence = await provider.GetRequiredService<IAiEvidenceReader>().GetEvidenceAsync("replay_list", null, null, null, Ct);
        await using var scope = provider.CreateAsyncScope();
        var ranking = await scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Messaging.IQueryHandler<XDrishti.Application.Reporting.GetBasketRankingQuery, XDrishti.Domain.Common.Result<XDrishti.Application.Reporting.BasketRankingView>>>()
            .HandleAsync(new XDrishti.Application.Reporting.GetBasketRankingQuery($"{Guid.NewGuid()},{Guid.NewGuid()}", null, null, null), Ct);
        await stop.CancelAsync();
        listener.Stop();
        await server;

        evidence.ErrorCode.ShouldBe(ToolErrorCodes.Unavailable);
        ranking.Error.Code.ShouldBe(ToolErrorCodes.Unavailable);
        lock (hits)
        {
            hits.Count.ShouldBe(2, "one request per client, the Location was not followed");
            hits.ShouldNotContain("/api/paper/shadow/summary");
        }
    }

    [Theory]
    [InlineData("GET", "http://xd-api:8080/api/replay/runs", true)]
    [InlineData("GET", "http://xd-api:8080/api/replay/runs/00000000-0000-7000-8000-0000000000a1", true)]
    [InlineData("GET", "http://xd-api:8080/api/confidence/scorecards/ORB/versions/2", true)]
    [InlineData("GET", "http://xd-api:8080/api/paper/shadow/summary", true)]
    [InlineData("POST", "http://xd-api:8080/api/replay/runs", false)]
    [InlineData("PUT", "http://xd-api:8080/api/paper/shadow/settings", false)]
    [InlineData("GET", "http://xd-api:8080/api/paper/shadow/settings", false)]
    [InlineData("GET", "http://xd-api:8080/api/replay/runs/00000000-0000-7000-8000-0000000000a1/trades", false)]
    [InlineData("GET", "http://xd-api:8080/api/replay/runs/00000000-0000-7000-8000-0000000000a1/sessions", false)]
    [InlineData("GET", "http://xd-api:8080/api/confidence/readiness-settings", false)]
    [InlineData("GET", "http://xd-api:8080/api/reports/baskets/ranking", false)]
    [InlineData("GET", "http://evil:8080/api/replay/runs", false)]
    [InlineData("GET", "http://user@xd-api:8080/api/replay/runs", false)]
    public void The_guard_allows_only_a_get_of_the_evidence_routes_on_the_configured_host(string method, string url, bool allowed) =>
        EvidenceApiEndpointGuard.IsAllowed(new HttpMethod(method), new Uri(url), Base).ShouldBe(allowed);
}
