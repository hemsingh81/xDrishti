using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// What the chart client sends to Dhan's Data API and how it reads the answers (made-up values; the token carries a marker that must appear nowhere but the header).
/// Time is a fake clock that the tests move on, so the throttle and the retry backoff do not wait.
/// </summary>
public sealed class DhanIntradayChartClientTests : IDisposable
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    /// <summary>Monday 2026-10-12 12:00:30 India time.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 30, TimeSpan.Zero);

    /// <summary>Wednesday 2026-10-07 09:15:00 India time.</summary>
    private static readonly DateTimeOffset From = new(2026, 10, 7, 3, 45, 0, TimeSpan.Zero);

    private static readonly FetchWindow Window = new(From, Now);

    private readonly StubBroker _broker = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private readonly AutoAdvancingClock _waits;

    private readonly CapturingLogger _log = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DhanIntradayChartClientTests() => _waits = new AutoAdvancingClock(_clock);

    public void Dispose() => _broker.Dispose();

    private static SecretValue Token => SecretValue.TryCreate("token-" + Marker, out var value, out _) ? value : throw new InvalidOperationException();

    private static IntradayTarget Share { get; } = new(Guid.NewGuid(), "NSE:ABB", "share", "13611", "NSE_EQ", [Window]);

    private DhanIntradayChartClient Client(int requestsPerSecond = 4) => new(_broker, new RequestThrottle(requestsPerSecond, _waits), _waits, _log);

    /// <summary>Runs the call; the throttle and the retry backoff end at once because the clock moves on by what they wait for.</summary>
    private async Task<ChartWindowResult> FetchAsync(IntradayTarget? target = null, FetchWindow? window = null, DhanIntradayChartClient? client = null) =>
        await (client ?? Client()).FetchAsync(Token, target ?? Share, window ?? Window, Ct);

    private static string Epoch(int day, int hour, int minute) => new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>The documented shape: parallel arrays; three bars of 2026-10-09 starting 09:15 India time (03:45 UTC).</summary>
    private static string Answer(string timestamps = "", bool oi = true)
    {
        var stamps = timestamps.Length > 0 ? timestamps : string.Join(',', Epoch(9, 3, 45), Epoch(9, 3, 46), Epoch(9, 3, 47));
        return $$"""
            {"open":[100.5,101,100.25],"high":[102,101.5,101],"low":[100,100.75,100],"close":[101,100.9,100.8],"volume":[1200,0,350],"timestamp":[{{stamps}}]{{(oi ? ",\"open_interest\":[0,0,0]" : string.Empty)}}}
            """;
    }

    private static string Error(string code) => $$"""{"errorType":"Data_API_Error","errorCode":"{{code}}","errorMessage":"something"}""";

    [Fact]
    public async Task It_posts_the_documented_json_body_with_the_token_header_and_nothing_else()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer());

        var result = await FetchAsync();

        result.Failure.ShouldBeNull();
        var request = _broker.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/charts/intraday");
        request.Headers["access-token"].ShouldBe("token-" + Marker);
        request.Headers["Content-Type"].ShouldBe("application/json");
        request.Headers.Keys.ShouldNotContain("client-id");
        request.Headers.Keys.Where(k => k.Contains("secret", StringComparison.OrdinalIgnoreCase) || k.StartsWith("app_", StringComparison.Ordinal)).ShouldBeEmpty();
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        root.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["exchangeSegment", "fromDate", "instrument", "interval", "oi", "securityId", "toDate"]);
        root.GetProperty("securityId").ValueKind.ShouldBe(JsonValueKind.String); // text, not a number
        root.GetProperty("securityId").GetString().ShouldBe("13611");
        (root.GetProperty("exchangeSegment").GetString(), root.GetProperty("instrument").GetString()).ShouldBe(("NSE_EQ", "EQUITY"));
        root.GetProperty("interval").ValueKind.ShouldBe(JsonValueKind.Number);
        root.GetProperty("interval").GetInt32().ShouldBe(1);
        root.GetProperty("oi").ValueKind.ShouldBe(JsonValueKind.False);
    }

    [Fact]
    public async Task The_period_is_sent_in_india_time_as_year_month_day_hours_minutes_seconds()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer());

        await FetchAsync();

        using var body = JsonDocument.Parse(_broker.Requests.Single().Body!);
        body.RootElement.GetProperty("fromDate").GetString().ShouldBe("2026-10-07 09:15:00"); // 03:45 UTC
        body.RootElement.GetProperty("toDate").GetString().ShouldBe("2026-10-12 12:00:30"); // now, to the second
    }

    [Theory]
    [InlineData("share", "NSE_EQ", "EQUITY")]
    [InlineData("etf", "NSE_EQ", "EQUITY")]
    [InlineData("index", "IDX_I", "INDEX")]
    public async Task The_segment_comes_from_the_master_and_the_instrument_from_the_type(string type, string segment, string instrument)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer());

        await FetchAsync(Share with { Type = type, Segment = segment });

        using var body = JsonDocument.Parse(_broker.Requests.Single().Body!);
        (body.RootElement.GetProperty("exchangeSegment").GetString(), body.RootElement.GetProperty("instrument").GetString()).ShouldBe((segment, instrument));
    }

    [Theory]
    [InlineData("share", "IDX_I")]
    [InlineData("index", "NSE_EQ")]
    [InlineData("share", "BSE_EQ")]
    [InlineData("future", "NSE_FNO")]
    [InlineData("share", "")]
    public async Task A_type_and_segment_that_do_not_belong_together_are_not_guessed_and_nothing_is_sent(string type, string segment)
    {
        var result = await FetchAsync(Share with { Type = type, Segment = segment });

        result.Failure!.Kind.ShouldBe(IntradayFailureKind.Failed);
        result.Failure.Message.ShouldContain("no usable Dhan segment");
        result.Requests.ShouldBe(0);
        _broker.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_arrays_are_read_into_bars_including_zero_volume_and_ignoring_open_interest()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer());

        var result = await FetchAsync();

        result.Requests.ShouldBe(1);
        result.Bars.ShouldBe(
        [
            new Bar(1_791_517_500, 100.5, 102, 100, 101, 1200),
            new Bar(1_791_517_560, 101, 101.5, 100.75, 100.9, 0),
            new Bar(1_791_517_620, 100.25, 101, 100, 100.8, 350),
        ]);
        result.Bars[0].Start.ShouldBe(new DateTimeOffset(2026, 10, 9, 3, 45, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Timestamps_with_thirteen_digits_are_milliseconds_and_ten_digits_are_seconds()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer("1791517500000,1791517560000,1791517620000.0"));
        var milliseconds = await FetchAsync();
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer("1791517500,1791517560,1791517620.0"));
        var seconds = await FetchAsync();

        milliseconds.Bars.Select(b => b.EpochSeconds).ShouldBe([1_791_517_500L, 1_791_517_560L, 1_791_517_620L]);
        seconds.Bars.Select(b => b.EpochSeconds).ShouldBe([1_791_517_500L, 1_791_517_560L, 1_791_517_620L]);
    }

    [Fact]
    public async Task Empty_arrays_are_no_bars_not_an_error()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, """{"open":[],"high":[],"low":[],"close":[],"volume":[],"timestamp":[],"open_interest":[]}""");

        var result = await FetchAsync();

        result.Failure.ShouldBeNull();
        result.Bars.ShouldBeEmpty();
        result.Requests.ShouldBe(1);
    }

    [Theory]
    [InlineData("""{"open":[1,2],"high":[1,2],"low":[1,2],"close":[1,2],"volume":[1],"timestamp":[1791517500,1791517560]}""", "different lengths")]
    [InlineData("""{"open":[1],"high":[1],"low":[1],"close":[1],"volume":[1],"timestamp":[1791517500,1791517560]}""", "different lengths")]
    [InlineData("""{"open":[1],"high":[1],"low":[1],"close":[1],"volume":[1]}""", "lacks the 'timestamp' array")]
    [InlineData("""{"open":[1],"high":[1],"low":[1],"close":[1],"timestamp":[1791517500]}""", "lacks the 'volume' array")]
    [InlineData("""{"open":"x","high":[1],"low":[1],"close":[1],"volume":[1],"timestamp":[1791517500]}""", "lacks the 'open' array")]
    [InlineData("""{"open":[null],"high":[1],"low":[1],"close":[1],"volume":[1],"timestamp":[1791517500]}""", "not a number")]
    [InlineData("""{"open":[1],"high":["1"],"low":[1],"close":[1],"volume":[1],"timestamp":[1791517500]}""", "not a number")]
    [InlineData("""{"open":[1],"high":[1],"low":[1],"close":[1],"volume":[1],"timestamp":[1e300]}""", "out of range")]
    [InlineData("""{"open":[1],"high":[1],"low":[1],"close":[1],"volume":[1],"timestamp":[99999999999999999]}""", "out of range")]
    [InlineData("""[1,2,3]""", "not what was expected")]
    [InlineData("""{"status":"success"}""", "lacks the 'open' array")]
    public async Task A_malformed_answer_is_a_failure_of_the_instrument_with_a_safe_message(string json, string expected)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, json);

        var result = await FetchAsync();

        result.Failure!.Kind.ShouldBe(IntradayFailureKind.Failed);
        result.Failure.Message.ShouldContain(expected);
        result.Bars.ShouldBeEmpty();
        result.Requests.ShouldBe(1); // a malformed answer is not retried
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("<html>oops</html>")]
    public async Task An_answer_that_is_not_json_is_a_failure_not_an_exception(string body)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, body);

        var result = await FetchAsync();

        result.Failure.ShouldBe(new IntradayFailure(IntradayFailureKind.Failed, "window 2026-10-07 to 2026-10-12: Dhan's answer could not be read"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Client ID or user generated access token is invalid or expired."}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-901"}""")] // the code counts, whatever the 4xx status
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-902","errorMessage":"not subscribed to Data APIs"}""")]
    [InlineData(HttpStatusCode.NotFound, """{"errorCode":"DH-901"}""")]
    public async Task A_refused_token_or_subscription_is_unauthorized_and_is_not_retried(HttpStatusCode status, string body)
    {
        _broker.Respond = _ => StubBroker.Json(status, body);

        var result = await FetchAsync();

        result.Failure!.Kind.ShouldBe(IntradayFailureKind.Unauthorized);
        result.Requests.ShouldBe(1);
        _broker.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-905"}""", "HTTP 400, DH-905")]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-907"}""", "HTTP 400, DH-907")] // "no data" is ambiguous here (bad parameters too): a visible failure, not an empty answer
    [InlineData(HttpStatusCode.NotFound, "", "HTTP 404")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "<html/>", "HTTP 422")]
    public async Task Other_client_errors_fail_the_instrument_and_are_never_retried(HttpStatusCode status, string body, string expected)
    {
        _broker.Respond = _ => StubBroker.Json(status, body);

        var result = await FetchAsync();

        result.Failure!.Kind.ShouldBe(IntradayFailureKind.Failed);
        result.Failure.Message.ShouldBe($"window 2026-10-07 to 2026-10-12: Dhan answered {expected}");
        result.Requests.ShouldBe(1);
        _broker.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Too_many_requests_is_tried_once_more_after_a_backoff_and_then_succeeds()
    {
        var times = new List<DateTimeOffset>();
        _broker.Respond = _ =>
        {
            times.Add(_clock.GetUtcNow());
            return times.Count == 1 ? StubBroker.Json(HttpStatusCode.TooManyRequests, Error("DH-904")) : StubBroker.Json(HttpStatusCode.OK, Answer());
        };

        var result = await FetchAsync();

        result.Failure.ShouldBeNull();
        result.Bars.Count.ShouldBe(3);
        result.Requests.ShouldBe(2);
        (times[1] - times[0]).ShouldBeGreaterThanOrEqualTo(DhanIntradayChartClient.RetryBackoff);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"errorCode":"DH-908"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-904"}""")] // Dhan's rate-limit code counts whatever the status
    public async Task A_busy_or_failing_dhan_is_asked_at_most_twice_and_then_the_instrument_fails(HttpStatusCode status, string body)
    {
        _broker.Respond = _ => StubBroker.Json(status, body);

        var result = await FetchAsync();

        result.Failure!.Kind.ShouldBe(IntradayFailureKind.Failed);
        result.Failure.Message.ShouldStartWith("window 2026-10-07 to 2026-10-12: Dhan answered HTTP ");
        result.Requests.ShouldBe(2);
        _broker.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_server_error_followed_by_a_good_answer_is_fine_and_a_token_refusal_on_the_retry_still_ends_the_phase()
    {
        var calls = 0;
        _broker.Respond = _ => ++calls == 1 ? StubBroker.Json(HttpStatusCode.InternalServerError, "") : StubBroker.Json(HttpStatusCode.Unauthorized, Error("DH-901"));

        var result = await FetchAsync();

        result.Failure!.Kind.ShouldBe(IntradayFailureKind.Unauthorized);
        result.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_is_a_failure()
    {
        _broker.Respond = _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example.invalid/") } };

        var result = await FetchAsync();

        result.Failure!.Message.ShouldBe("window 2026-10-07 to 2026-10-12: Dhan answered with a redirect (HTTP 302), which is not followed");
        result.Requests.ShouldBe(1);
        _broker.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_timeout_or_network_failure_fails_the_instrument_and_a_cancellation_is_not_hidden()
    {
        _broker.Respond = _ => throw new TaskCanceledException("timed out");
        var timedOut = await FetchAsync();
        _broker.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused https://api.dhan.co/v2/charts/intraday");
        var unreachable = await FetchAsync();

        timedOut.Failure!.Message.ShouldBe("window 2026-10-07 to 2026-10-12: Dhan did not answer in time");
        unreachable.Failure!.Message.ShouldBe("window 2026-10-07 to 2026-10-12: Dhan could not be reached (ConnectionError)");
        unreachable.Failure.Message.ShouldNotContain("api.dhan.co");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _broker.Respond = _ => throw new TaskCanceledException();
        await Should.ThrowAsync<OperationCanceledException>(async () => await Client().FetchAsync(Token, Share, Window, cancelled.Token));
    }

    [Fact]
    public async Task Requests_are_spaced_by_the_shared_throttle()
    {
        var times = new List<DateTimeOffset>();
        _broker.Respond = _ =>
        {
            times.Add(_clock.GetUtcNow());
            return StubBroker.Json(HttpStatusCode.OK, Answer());
        };
        var client = Client(requestsPerSecond: 4);

        await FetchAsync(client: client);
        await FetchAsync(client: client);
        await FetchAsync(client: client);

        times.Count.ShouldBe(3);
        (times[1] - times[0]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        (times[2] - times[1]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task The_retry_goes_through_the_throttle_again()
    {
        var times = new List<DateTimeOffset>();
        _broker.Respond = _ =>
        {
            times.Add(_clock.GetUtcNow());
            return StubBroker.Json(HttpStatusCode.InternalServerError, "");
        };

        await FetchAsync(client: Client(requestsPerSecond: 1));

        (times[1] - times[0]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.OK)]
    public async Task A_failure_carries_no_text_of_the_answer_and_no_malformed_code(HttpStatusCode status)
    {
        const string Upstream = "UPSTREAM-BODY-MARKER";
        _broker.Respond = _ => StubBroker.Json(status, $$"""{"errorType":"{{Upstream}}","errorCode":"{{Upstream}}","errorMessage":"{{Upstream}} token-{{Marker}}","open":"{{Upstream}}"}""");

        var result = await FetchAsync();

        result.Failure.ShouldNotBeNull();
        result.Failure.Message.ShouldNotContain(Upstream);
        result.Failure.Message.ShouldNotContain(Marker);
        result.Failure.ToString().ShouldNotContain(Upstream);
        string.Join('\n', _log.Messages).ShouldNotContain(Upstream);
    }

    [Fact]
    public async Task The_token_the_url_and_the_answer_are_never_logged_or_put_into_a_message()
    {
        var messages = new List<string>();
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.Unauthorized, $$"""{"errorCode":"DH-901","errorMessage":"token-{{Marker}} is invalid","holder":"Example Holder"}""");
        messages.Add((await FetchAsync()).Failure!.Message);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, $$"""{"errorCode":"DH-905","errorMessage":"securityId 13611 token-{{Marker}}"}""");
        messages.Add((await FetchAsync()).Failure!.Message);
        _broker.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, $"refused https://api.dhan.co/v2/charts/intraday token-{Marker}");
        messages.Add((await FetchAsync()).Failure!.Message);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer());
        await FetchAsync();

        var logged = string.Join('\n', _log.Messages);
        logged.ShouldContain("DH-901");
        logged.ShouldContain("DH-905");
        foreach (var text in messages.Append(logged))
        {
            text.ShouldNotContain(Marker);
            text.ShouldNotContain("api.dhan.co");
            text.ShouldNotContain("Example Holder");
            text.ShouldNotContain("13611");
        }
    }

    [Fact]
    public async Task The_allow_list_guard_allows_only_the_one_chart_request_on_the_chart_client()
    {
        using var client = _broker.CreateClient(BrokerHttpClientNames.DhanChart);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer());

        foreach (var url in new[]
        {
            "https://api.dhan.co/v2/charts/historical", "https://api.dhan.co/v2/charts/intraday?x=1", "https://api.dhan.co/v2/orders", "https://api.dhan.co/v2/positions",
            "https://api.dhan.co/v2/fundlimit", "https://api.dhan.co/v2/holdings/x", "https://auth.dhan.co/v2/charts/intraday", "http://api.dhan.co/v2/charts/intraday",
        })
        {
            using var post = new HttpRequestMessage(HttpMethod.Post, new Uri(url)) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.SendAsync(post, Ct));
        }

        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/charts/intraday"), Ct));
        _broker.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void The_chart_client_has_a_15_second_timeout_and_accepts_answers_up_to_16_MB()
    {
        using var client = _broker.CreateClient(BrokerHttpClientNames.DhanChart);

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(15));
        client.MaxResponseContentBufferSize.ShouldBe(16 * 1024 * 1024);
    }

    private sealed class CapturingLogger : ILogger<DhanIntradayChartClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
