using System.Net;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// What the option-chain client sends to Dhan's two option-chain endpoints and how it names the answers (P14-02, ADR 0176): URL, method, headers, body, no query, the one throttle across both endpoints (never closer than 3 seconds) and every class of failure
/// as an outcome word. The token and the client id carry markers that must appear nowhere but the headers. The shapes are written from Dhan's documentation (docs/research/dhan-samples/optionchain-*.json, UNVERIFIED until the pilot).
/// </summary>
public sealed class DhanOptionChainClientTests : IDisposable
{
    private const string TokenMarker = "FAKE-TOKEN-MARKER-DO-NOT-LEAK";

    private const string ClientMarker = "FAKE-CLIENT-1000000001";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 5, 0, 0, TimeSpan.Zero);

    private static readonly ChainRequest Chain = new(ChainKind.Chain, 13, "IDX_I", new DateOnly(2026, 11, 3));

    private static readonly ChainRequest Expiries = new(ChainKind.Expiries, 13, "IDX_I", null);

    private readonly StubBroker _broker = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private readonly FakeCredentials _credentials = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private DhanOptionChainClient Client(ChainThrottle? throttle = null) => new(_broker, _credentials, throttle ?? new ChainThrottle(new AutoAdvancingClock(_clock)), OptionChainSettings.Off, _clock);

    [Fact]
    public async Task A_chain_request_posts_the_documented_body_to_the_exact_url_with_the_two_headers_and_no_query()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "{\"data\":{},\"status\":\"success\"}");

        var reply = await Client().SendAsync(Chain, TimeSpan.FromSeconds(3), Ct);

        var request = _broker.Requests.Single();
        (request.Method, request.Uri.AbsoluteUri).ShouldBe((HttpMethod.Post, "https://api.dhan.co/v2/optionchain"));
        request.Uri.Query.ShouldBeEmpty();
        request.Headers["access-token"].ShouldBe("token-" + TokenMarker);
        request.Headers["client-id"].ShouldBe(ClientMarker);
        request.Headers["Content-Type"].ShouldBe("application/json");
        request.Body.ShouldBe("""{"UnderlyingScrip":13,"UnderlyingSeg":"IDX_I","Expiry":"2026-11-03"}""");
        (reply.Outcome, reply.HttpStatus, reply.DhanCode).ShouldBe((ChainOutcomes.Ok, 200, null));
        reply.Body.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_expiry_list_request_posts_the_body_without_an_expiry_to_its_own_exact_url()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "{\"data\":[\"2026-11-03\"],\"status\":\"success\"}");

        await Client().SendAsync(new ChainRequest(ChainKind.Expiries, 2885, "NSE_EQ", new DateOnly(2026, 11, 3)), TimeSpan.FromSeconds(3), Ct);

        var request = _broker.Requests.Single();
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/optionchain/expirylist");
        request.Body.ShouldBe("""{"UnderlyingScrip":2885,"UnderlyingSeg":"NSE_EQ"}""");
    }

    [Fact]
    public async Task No_two_requests_of_either_kind_are_closer_than_three_seconds_and_a_smaller_spacing_is_raised_to_three()
    {
        var times = new List<DateTimeOffset>();
        _broker.Respond = _ =>
        {
            times.Add(_clock.GetUtcNow());
            return StubBroker.Json(HttpStatusCode.OK, "{}");
        };
        var client = Client();

        await client.SendAsync(Chain, TimeSpan.FromSeconds(3), Ct);
        await client.SendAsync(Expiries, TimeSpan.FromMilliseconds(100), Ct);
        await client.SendAsync(Chain, TimeSpan.Zero, Ct);
        await client.SendAsync(Expiries, TimeSpan.FromSeconds(3), Ct);

        times.Count.ShouldBe(4);
        times.Zip(times.Skip(1), (a, b) => b - a).ShouldAllBe(gap => gap >= TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task A_larger_spacing_after_a_rate_limit_is_kept_between_the_two_requests()
    {
        var times = new List<DateTimeOffset>();
        _broker.Respond = _ =>
        {
            times.Add(_clock.GetUtcNow());
            return StubBroker.Json(HttpStatusCode.OK, "{}");
        };
        var client = Client();

        await client.SendAsync(Chain, TimeSpan.FromSeconds(3), Ct);
        await client.SendAsync(Chain, TimeSpan.FromSeconds(6), Ct);

        (times[1] - times[0]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(6));
    }

    [Theory]
    [InlineData(429, null, ChainOutcomes.RateLimited)]
    [InlineData(400, "DH-904", ChainOutcomes.RateLimited)]
    [InlineData(401, null, ChainOutcomes.TokenRefused)]
    [InlineData(403, null, ChainOutcomes.TokenRefused)]
    [InlineData(400, "DH-901", ChainOutcomes.TokenRefused)]
    [InlineData(403, "DH-902", ChainOutcomes.NotSubscribed)]
    [InlineData(400, "DH-907", ChainOutcomes.NoData)]
    [InlineData(500, "DH-908", ChainOutcomes.ServerError)]
    [InlineData(500, "DH-909", ChainOutcomes.ServerError)]
    [InlineData(503, null, ChainOutcomes.ServerError)]
    [InlineData(302, null, ChainOutcomes.ServerError)]
    public async Task Each_failure_answer_is_an_outcome_word_with_the_status_and_the_code_and_no_body(int status, string? code, string outcome)
    {
        var body = code is null ? "{}" : $$"""{"errorType":"x","errorCode":"{{code}}","errorMessage":"secret text {{TokenMarker}}"}""";
        _broker.Respond = _ => StubBroker.Json((HttpStatusCode)status, body);

        var reply = await Client().SendAsync(Chain, TimeSpan.FromSeconds(3), Ct);

        (reply.Outcome, reply.HttpStatus, reply.DhanCode, reply.Body).ShouldBe((outcome, status, code, null));
        reply.ToString().ShouldNotContain(TokenMarker, Case.Sensitive, "no text of Dhan's answer is kept");
    }

    [Fact]
    public async Task A_network_failure_is_a_server_error_that_does_not_name_the_url_or_the_token()
    {
        _broker.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "could not connect to https://api.dhan.co/v2/optionchain?secret=" + TokenMarker);

        var reply = await Client().SendAsync(Chain, TimeSpan.FromSeconds(3), Ct);

        (reply.Outcome, reply.HttpStatus, reply.Body).ShouldBe((ChainOutcomes.ServerError, null, null));
        reply.ToString().ShouldNotContain("dhan.co", Case.Insensitive);
        reply.ToString().ShouldNotContain(TokenMarker);
    }

    [Fact]
    public async Task A_timeout_that_is_not_the_callers_is_the_timeout_outcome()
    {
        _broker.Respond = _ => throw new TaskCanceledException("timed out");

        var reply = await Client().SendAsync(Chain, TimeSpan.FromSeconds(3), Ct);

        (reply.Outcome, reply.Body).ShouldBe((ChainOutcomes.Timeout, null));
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_thrown_not_reported()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _broker.Respond = _ => throw new TaskCanceledException("cancelled");

        await Should.ThrowAsync<OperationCanceledException>(async () => await Client().SendAsync(Chain, TimeSpan.FromSeconds(3), cancelled.Token));
    }

    [Fact]
    public async Task Without_a_usable_data_account_nothing_is_sent_and_no_throttle_slot_is_used()
    {
        _credentials.Value = null;
        var client = Client();

        var reply = await client.SendAsync(Chain, TimeSpan.FromSeconds(3), Ct);

        (reply.Outcome, reply.NoCredentials, reply.Body).ShouldBe((ChainOutcomes.TokenRefused, true, null));
        _broker.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_request_to_a_path_off_the_allow_list_would_be_refused_by_the_guard_so_the_two_urls_are_the_only_ones_used()
    {
        BrokerEndpoints.IsAllowed(HttpMethod.Post, BrokerEndpoints.DhanOptionChain).ShouldBeTrue();
        BrokerEndpoints.IsAllowed(HttpMethod.Post, BrokerEndpoints.DhanOptionChainExpiryList).ShouldBeTrue();
        BrokerEndpoints.DhanOptionChain.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/optionchain");
        BrokerEndpoints.DhanOptionChainExpiryList.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/optionchain/expirylist");
        await Task.CompletedTask;
    }

    [Fact]
    public void The_body_of_a_code_in_the_answer_is_read_for_the_code_only()
    {
        DhanOptionChainClient.CodeOf("""{"errorCode":"DH-904","errorMessage":"Too many requests"}"""u8.ToArray()).ShouldBe("DH-904");
        DhanOptionChainClient.CodeOf("""{"remarks":{"error_code":"DH-902"}}"""u8.ToArray()).ShouldBe("DH-902");
        DhanOptionChainClient.CodeOf("""{"errorCode":"807"}"""u8.ToArray()).ShouldBe("807");
        DhanOptionChainClient.CodeOf("""{"errorCode":"free text with the token FAKE"}"""u8.ToArray()).ShouldBeNull("anything but a well-formed code is not taken");
        DhanOptionChainClient.CodeOf("not json"u8.ToArray()).ShouldBeNull();
        DhanOptionChainClient.CodeOf("[]"u8.ToArray()).ShouldBeNull();
    }

    private sealed class FakeCredentials : IDataAccountCredentials
    {
        public DataAccountCredentials? Value { get; set; } = new(SecretValue.TryCreate("token-" + TokenMarker, out var token, out _) ? token : throw new InvalidOperationException(), ClientMarker);

        public ValueTask<DataAccountCredentials?> GetCredentialsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Value);
    }
}
