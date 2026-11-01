using System.Net;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// What the backfill client sends to Dhan's rolling-option endpoint and how it names the answers (P14-03, ADR 0177): URL, method, headers, body, no query, the one throttle (never closer than half a second) and every class of failure as an outcome word.
/// The token and the client id carry markers that must appear nowhere but the headers. The body shape is written from Dhan's documentation (docs/research/dhan-samples/rollingoption-success.json, UNVERIFIED until the owner's probe).
/// </summary>
public sealed class DhanRollingOptionClientTests : IDisposable
{
    private const string TokenMarker = "FAKE-TOKEN-MARKER-DO-NOT-LEAK";

    private const string ClientMarker = "FAKE-CLIENT-1000000001";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly RollingRequest Request = new(13, "OPTIDX", 5, 'W', 1, 10, "CE", new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 2));

    private readonly StubBroker _broker = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private readonly FakeCredentials _credentials = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private DhanRollingOptionClient Client(BackfillThrottle? throttle = null) => new(_broker, _credentials, throttle ?? new BackfillThrottle(new AutoAdvancingClock(_clock)), OptionBackfillSettings.Off, _clock);

    [Fact]
    public async Task A_request_posts_the_documented_body_to_the_exact_url_with_the_two_headers_and_no_query()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "{\"data\":{\"ce\":null,\"pe\":null}}");

        var reply = await Client().SendAsync(Request, TimeSpan.FromSeconds(1), Ct);

        var request = _broker.Requests.Single();
        (request.Method, request.Uri.AbsoluteUri).ShouldBe((HttpMethod.Post, "https://api.dhan.co/v2/charts/rollingoption"));
        request.Uri.Query.ShouldBeEmpty();
        request.Headers["access-token"].ShouldBe("token-" + TokenMarker);
        request.Headers["client-id"].ShouldBe(ClientMarker);
        request.Headers["Content-Type"].ShouldBe("application/json");
        request.Body.ShouldBe("""{"exchangeSegment":"NSE_FNO","interval":"5","securityId":13,"instrument":"OPTIDX","expiryFlag":"WEEK","expiryCode":1,"strike":"ATM+10","drvOptionType":"CALL","requiredData":["open","high","low","close","volume","strike","oi","iv","spot"],"fromDate":"2026-09-02","toDate":"2026-10-02"}""");
        (reply.Outcome, reply.HttpStatus, reply.DhanCode).ShouldBe((BackfillOutcomes.Ok, 200, null));
        reply.Body.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(10, "ATM+10")]
    [InlineData(3, "ATM+3")]
    [InlineData(0, "ATM")]
    [InlineData(-3, "ATM-3")]
    [InlineData(-10, "ATM-10")]
    public void The_strike_is_atm_plus_or_minus_the_offset(int offset, string expected)
    {
        DhanRollingOptionClient.Strike(offset).ShouldBe(expected);
    }

    [Fact]
    public void A_put_on_a_monthly_share_option_has_the_put_month_and_fifteen_minute_words()
    {
        var body = System.Text.Encoding.UTF8.GetString(DhanRollingOptionClient.Body(Request with { Side = "PE", ExpiryFlag = 'M', ExpiryCode = 2, Instrument = "OPTSTK", IntervalMinutes = 15, Offset = -3 }));

        body.ShouldContain("\"drvOptionType\":\"PUT\"");
        body.ShouldContain("\"expiryFlag\":\"MONTH\"");
        body.ShouldContain("\"expiryCode\":2");
        body.ShouldContain("\"instrument\":\"OPTSTK\"");
        body.ShouldContain("\"interval\":\"15\"");
        body.ShouldContain("\"strike\":\"ATM-3\"");
    }

    [Fact]
    public async Task No_two_requests_are_closer_than_the_spacing_and_a_smaller_spacing_is_raised_to_half_a_second()
    {
        var times = new List<DateTimeOffset>();
        _broker.Respond = _ =>
        {
            times.Add(_clock.GetUtcNow());
            return StubBroker.Json(HttpStatusCode.OK, "{}");
        };
        var client = Client();

        await client.SendAsync(Request, TimeSpan.FromSeconds(1), Ct);
        await client.SendAsync(Request, TimeSpan.FromMilliseconds(10), Ct);
        await client.SendAsync(Request, TimeSpan.Zero, Ct);
        await client.SendAsync(Request, TimeSpan.FromSeconds(2), Ct);

        times.Count.ShouldBe(4);
        var gaps = times.Zip(times.Skip(1), (a, b) => b - a).ToList();
        gaps[0].ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500));
        gaps[1].ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500));
        gaps[2].ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2), "a larger spacing after a rate limit applies to the very next request");
    }

    [Theory]
    [InlineData(429, null, BackfillOutcomes.RateLimited)]
    [InlineData(400, "DH-904", BackfillOutcomes.RateLimited)]
    [InlineData(401, null, BackfillOutcomes.TokenRefused)]
    [InlineData(403, null, BackfillOutcomes.TokenRefused)]
    [InlineData(400, "DH-901", BackfillOutcomes.TokenRefused)]
    [InlineData(403, "DH-902", BackfillOutcomes.NotSubscribed)]
    [InlineData(400, "DH-907", BackfillOutcomes.NoData)]
    [InlineData(500, "DH-908", BackfillOutcomes.ServerError)]
    [InlineData(500, "DH-909", BackfillOutcomes.ServerError)]
    [InlineData(503, null, BackfillOutcomes.ServerError)]
    [InlineData(302, null, BackfillOutcomes.ServerError)]
    public async Task Each_failure_answer_is_an_outcome_word_with_the_status_and_the_code_and_no_body(int status, string? code, string outcome)
    {
        var body = code is null ? "{}" : $$"""{"errorType":"x","errorCode":"{{code}}","errorMessage":"secret text {{TokenMarker}}"}""";
        _broker.Respond = _ => StubBroker.Json((HttpStatusCode)status, body);

        var reply = await Client().SendAsync(Request, TimeSpan.FromSeconds(1), Ct);

        (reply.Outcome, reply.HttpStatus, reply.DhanCode, reply.Body).ShouldBe((outcome, status, code, null));
        reply.ToString().ShouldNotContain(TokenMarker, Case.Sensitive, "no text of Dhan's answer is kept");
    }

    [Fact]
    public async Task A_network_failure_is_a_server_error_that_does_not_name_the_url_or_the_token()
    {
        _broker.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "could not connect to https://api.dhan.co/v2/charts/rollingoption?secret=" + TokenMarker);

        var reply = await Client().SendAsync(Request, TimeSpan.FromSeconds(1), Ct);

        (reply.Outcome, reply.HttpStatus, reply.Body).ShouldBe((BackfillOutcomes.ServerError, null, null));
        reply.ToString().ShouldNotContain("dhan.co", Case.Insensitive);
        reply.ToString().ShouldNotContain(TokenMarker);
    }

    [Fact]
    public async Task A_timeout_that_is_not_the_callers_is_the_timeout_outcome_and_the_callers_own_cancellation_is_thrown()
    {
        _broker.Respond = _ => throw new TaskCanceledException("timed out");
        (await Client().SendAsync(Request, TimeSpan.FromSeconds(1), Ct)).Outcome.ShouldBe(BackfillOutcomes.Timeout);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => await Client().SendAsync(Request, TimeSpan.FromSeconds(1), cancelled.Token));
    }

    [Fact]
    public async Task Without_a_usable_data_account_nothing_is_sent_and_the_reply_says_so()
    {
        _credentials.Value = null;

        var reply = await Client().SendAsync(Request, TimeSpan.FromSeconds(1), Ct);

        (reply.Outcome, reply.NoCredentials, reply.Body).ShouldBe((BackfillOutcomes.TokenRefused, true, null));
        _broker.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void The_url_is_the_one_allow_listed_constant_and_the_class_logs_nothing()
    {
        BrokerEndpoints.IsAllowed(HttpMethod.Post, BrokerEndpoints.DhanRollingOption).ShouldBeTrue();
        BrokerEndpoints.DhanRollingOption.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/charts/rollingoption");
        typeof(DhanRollingOptionClient).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ShouldNotContain(n => n.Contains("Logger", StringComparison.Ordinal));
    }

    [Fact]
    public void The_code_of_the_answer_is_read_for_the_code_only()
    {
        DhanRollingOptionClient.CodeOf("""{"errorCode":"DH-904","errorMessage":"Too many requests"}"""u8.ToArray()).ShouldBe("DH-904");
        DhanRollingOptionClient.CodeOf("""{"remarks":{"error_code":"DH-902"}}"""u8.ToArray()).ShouldBe("DH-902");
        DhanRollingOptionClient.CodeOf("""{"errorCode":"free text with the token FAKE"}"""u8.ToArray()).ShouldBeNull();
        DhanRollingOptionClient.CodeOf("not json"u8.ToArray()).ShouldBeNull();
    }

    private sealed class FakeCredentials : IDataAccountCredentials
    {
        public DataAccountCredentials? Value { get; set; } = new(SecretValue.TryCreate("token-" + TokenMarker, out var token, out _) ? token : throw new InvalidOperationException(), ClientMarker);

        public ValueTask<DataAccountCredentials?> GetCredentialsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Value);
    }
}
