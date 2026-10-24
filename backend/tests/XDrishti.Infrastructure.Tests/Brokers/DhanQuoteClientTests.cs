using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// What the quote client sends to Dhan's market-quote API and how it reads the answers (ADR 0019): headers, body, the split above 1,000 securities with the throttle between requests, the parsing and every
/// class of failure, with made-up values. The token and the client id carry markers that must appear nowhere but the headers (not in a log, not in a message). The shapes are written from Dhan's documentation
/// (docs/research/dhan-samples/quote-*.json, unverified until the owner's first real run).
/// </summary>
public sealed class DhanQuoteClientTests : IDisposable
{
    private const string TokenMarker = "FAKE-TOKEN-MARKER-DO-NOT-LEAK";

    private const string ClientMarker = "FAKE-CLIENT-1000000001";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 5, 0, 0, TimeSpan.Zero);

    private readonly StubBroker _broker = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private readonly AutoAdvancingClock _waits;

    private readonly CapturingLogger _log = new();

    private readonly FakeCredentials _credentials = new();

    public DhanQuoteClientTests() => _waits = new AutoAdvancingClock(_clock);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private DhanQuoteClient Client() => new(_broker, _credentials, new QuoteThrottle(_waits), _log);

    private Task<QuoteResult> QuoteAsync(params string[] ids) => Client().QuoteAsync(ids, Ct).AsTask();

    private static string Answer(params (string Id, string Entry)[] entries) =>
        "{\"data\":{\"NSE_EQ\":{" + string.Join(',', entries.Select(e => $"\"{e.Id}\":{e.Entry}")) + "}},\"status\":\"success\"}";

    private static string Entry(string last, string close = "98.5") => $$"""{"average_price":0,"buy_quantity":0,"depth":{"buy":[],"sell":[]},"last_price":{{last}},"last_quantity":0,"net_change":0,"ohlc":{"close":{{close}},"high":0,"low":0,"open":0} }""";

    [Fact]
    public async Task It_posts_the_documented_body_with_the_token_and_client_id_headers_and_reads_price_and_previous_close()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer(("1333", Entry("101.25", "99.5")), ("11536", Entry("4520", "4506"))));

        var result = await QuoteAsync("1333", "11536");

        result.Failure.ShouldBeNull();
        result.Requests.ShouldBe(1);
        result.Quotes.ShouldBe([new SecurityQuote("1333", 101.25m, 99.5m), new SecurityQuote("11536", 4520m, 4506m)]);
        var request = _broker.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/marketfeed/quote");
        request.Headers["access-token"].ShouldBe("token-" + TokenMarker);
        request.Headers["client-id"].ShouldBe(ClientMarker);
        request.Headers["Content-Type"].ShouldBe("application/json");
        request.Body.ShouldBe("""{"NSE_EQ":[1333,11536]}""");
    }

    [Fact]
    public async Task No_ids_cost_no_request_and_non_numeric_or_duplicate_ids_are_never_sent()
    {
        (await QuoteAsync()).Quotes.ShouldBeEmpty();
        _broker.Requests.ShouldBeEmpty();
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer(("1333", Entry("10"))));

        await QuoteAsync("1333", "1333", "abc", "12 34", "-5", "", "0");

        _broker.Requests.Single().Body.ShouldBe("""{"NSE_EQ":[1333]}""");
    }

    [Fact]
    public async Task More_than_a_thousand_securities_are_split_into_requests_at_least_a_second_apart()
    {
        var times = new List<DateTimeOffset>();
        _broker.Respond = _ =>
        {
            times.Add(_clock.GetUtcNow());
            var first = JsonDocument.Parse(_broker.Requests[^1].Body!).RootElement.GetProperty("NSE_EQ")[0].GetInt64().ToString(CultureInfo.InvariantCulture);
            return StubBroker.Json(HttpStatusCode.OK, Answer((first, Entry("10"))));
        };
        var ids = Enumerable.Range(1, 2500).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();

        var result = await QuoteAsync(ids);

        result.Requests.ShouldBe(3);
        _broker.Requests.Count.ShouldBe(3);
        _broker.Requests.Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("NSE_EQ").GetArrayLength()).ShouldBe([1000, 1000, 500]);
        times.Zip(times.Skip(1), (a, b) => b - a).ShouldAllBe(gap => gap >= TimeSpan.FromSeconds(1));
        result.Quotes.Select(q => q.SecurityId).ShouldBe(["1", "1001", "2001"]); // the first security of each request
    }

    [Fact]
    public async Task A_security_missing_from_the_answer_or_with_a_bad_price_is_left_out_for_that_security_only()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer(
            ("1", Entry("10", "9")),
            ("2", Entry("0", "9")),
            ("3", Entry("-4", "9")),
            ("4", Entry("\"12.5\"", "9")),
            ("5", Entry("null", "9")),
            ("6", "{}"),
            ("7", "\"x\""),
            ("99", Entry("50", "49"))));

        var result = await QuoteAsync("1", "2", "3", "4", "5", "6", "7", "8");

        result.Failure.ShouldBeNull();
        result.Quotes.ShouldBe([new SecurityQuote("1", 10m, 9m)]); // 8 is missing, 99 was not asked for
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"98\"")]
    [InlineData("null")]
    public async Task A_previous_close_that_is_not_a_positive_number_is_no_previous_close_and_the_price_still_counts(string close)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer(("1", Entry("10", close))));

        var result = await QuoteAsync("1");

        result.Quotes.ShouldBe([new SecurityQuote("1", 10m, null)]);
    }

    [Theory]
    [InlineData("\"last_trade_time\":\"26/10/2026 09:09:30\"", 2026, 10, 26, 3, 39, 30)]
    [InlineData("\"last_trade_time\":1792985970", 2026, 10, 26, 3, 39, 30)]
    public async Task The_last_trade_time_and_volume_of_the_same_answer_travel_with_the_quote(string timeField, int year, int month, int day, int hour, int minute, int second)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer(("1", "{\"last_price\":10,\"volume\":1234," + timeField + ",\"ohlc\":{\"close\":9}}")));

        var quote = (await QuoteAsync("1")).Quotes.Single();

        quote.LastTradeTime.ShouldBe(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));
        quote.Volume.ShouldBe(1234);
        quote.PreviousClose.ShouldBe(9m);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"last_trade_time\":null")]
    [InlineData(",\"last_trade_time\":\"yesterday\"")]
    [InlineData(",\"last_trade_time\":\"2026-10-26T09:09:30\"")]
    [InlineData(",\"last_trade_time\":0")]
    [InlineData(",\"last_trade_time\":true")]
    public async Task A_missing_or_unreadable_trade_time_is_null_and_the_price_still_counts(string field)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer(("1", "{\"last_price\":10" + field + "}")));

        var quote = (await QuoteAsync("1")).Quotes.Single();

        quote.LastPrice.ShouldBe(10m);
        quote.LastTradeTime.ShouldBeNull();
        quote.Volume.ShouldBeNull();
    }

    [Fact]
    public async Task A_security_without_ohlc_has_a_price_and_no_previous_close()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Answer(("1", """{"last_price":10}""")));

        (await QuoteAsync("1")).Quotes.ShouldBe([new SecurityQuote("1", 10m, null)]);
    }

    [Fact]
    public async Task An_answer_without_the_nse_segment_has_no_quotes_but_is_no_failure()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, """{"data":{},"status":"success"}""");

        var result = await QuoteAsync("1");

        result.Failure.ShouldBeNull();
        result.Quotes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task A_401_or_403_means_the_token_was_refused(int status)
    {
        _broker.Respond = _ => StubBroker.Json((HttpStatusCode)status, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"secret text"}""");

        var result = await QuoteAsync("1");

        result.Failure.ShouldBe(new QuoteFailure(QuoteFailureKind.Unauthorized, DhanQuoteClient.TokenRefused));
        result.Quotes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("DH-901")]
    [InlineData("DH-902")]
    public async Task A_client_error_that_carries_the_token_or_subscription_code_is_a_refusal_too(string code)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, $$"""{"errorCode":"{{code}}"}""");

        (await QuoteAsync("1")).Failure!.Kind.ShouldBe(QuoteFailureKind.Unauthorized);
    }

    [Fact]
    public async Task A_429_is_rate_limited_and_not_retried()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.TooManyRequests, """{"errorCode":"DH-904"}""");

        var result = await QuoteAsync("1");

        result.Failure.ShouldBe(new QuoteFailure(QuoteFailureKind.RateLimited, DhanQuoteClient.RateLimitReached));
        _broker.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(400, "Dhan's quote service answered HTTP 400.")]
    [InlineData(404, "Dhan's quote service answered HTTP 404.")]
    [InlineData(500, "Dhan's quote service answered HTTP 500.")]
    [InlineData(503, "Dhan's quote service answered HTTP 503.")]
    public async Task Any_other_error_status_fails_with_a_fixed_message(int status, string message)
    {
        _broker.Respond = _ => StubBroker.Json((HttpStatusCode)status, """{"remarks":"anything at all"}""");

        var result = await QuoteAsync("1");

        result.Failure.ShouldBe(new QuoteFailure(QuoteFailureKind.Failed, message));
        _broker.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_dhan_error_code_is_named_in_the_message_only_in_its_documented_form()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, """{"remarks":{"error_code":"DH-905","error_message":"bad"}}""");
        (await QuoteAsync("1")).Failure!.Message.ShouldBe("Dhan's quote service answered HTTP 400, DH-905.");

        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, $$"""{"errorCode":"{{TokenMarker}}"}""");
        (await QuoteAsync("1")).Failure!.Message.ShouldBe("Dhan's quote service answered HTTP 400.");
    }

    [Fact]
    public async Task A_redirect_is_not_followed()
    {
        _broker.Respond = _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example.invalid/") } };

        var result = await QuoteAsync("1");

        result.Failure!.Kind.ShouldBe(QuoteFailureKind.Failed);
        result.Failure.Message.ShouldContain("redirect");
        result.Failure.Message.ShouldNotContain("evil");
        _broker.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"status":"success"}""")]
    [InlineData("""{"data":[],"status":"success"}""")]
    [InlineData("""{"data":{"NSE_EQ":[]},"status":"success"}""")]
    [InlineData("""{"data":{"NSE_EQ":{}},"status":"failed"}""")]
    public async Task An_unreadable_or_unexpected_answer_fails_with_the_fixed_message(string body)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, body);

        var result = await QuoteAsync("1");

        result.Failure.ShouldBe(new QuoteFailure(QuoteFailureKind.Failed, DhanQuoteClient.Unexpected));
        result.Quotes.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_answer_longer_than_the_limit_is_an_unexpected_answer_not_a_network_failure()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "{\"data\":{\"NSE_EQ\":{},\"pad\":\"" + new string('x', (4 * 1024 * 1024) + 10) + "\"},\"status\":\"success\"}");

        var result = await QuoteAsync("1");

        result.Failure.ShouldBe(new QuoteFailure(QuoteFailureKind.Failed, DhanQuoteClient.Unexpected));
        result.Quotes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_timeout_fails_with_a_fixed_message()
    {
        _broker.Respond = _ => throw new TaskCanceledException("timed out");

        var result = await QuoteAsync("1");

        result.Failure.ShouldBe(new QuoteFailure(QuoteFailureKind.Failed, DhanQuoteClient.TimedOut));
    }

    [Fact]
    public async Task A_network_failure_fails_without_naming_the_url()
    {
        _broker.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "could not connect to https://api.dhan.co/v2/marketfeed/quote?secret=" + TokenMarker);

        var result = await QuoteAsync("1");

        result.Failure!.Kind.ShouldBe(QuoteFailureKind.Failed);
        result.Failure.Message.ShouldBe("Dhan could not be reached (ConnectionError).");
    }

    [Fact]
    public async Task A_request_that_fails_part_way_leaves_no_half_answer()
    {
        var calls = 0;
        _broker.Respond = _ => ++calls == 1
            ? StubBroker.Json(HttpStatusCode.OK, Answer(("1", Entry("10", "9"))))
            : StubBroker.Json(HttpStatusCode.ServiceUnavailable, "{}");
        var ids = Enumerable.Range(1, 1500).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();

        var result = await QuoteAsync(ids);

        result.Quotes.ShouldBeEmpty();
        result.Failure!.Kind.ShouldBe(QuoteFailureKind.Failed);
        result.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task Without_a_usable_data_account_nothing_is_sent()
    {
        _credentials.Value = null;

        var result = await QuoteAsync("1");

        result.Failure.ShouldBe(new QuoteFailure(QuoteFailureKind.Unauthorized, DhanQuoteClient.NoToken));
        _broker.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Neither_the_token_nor_the_client_id_nor_the_answer_is_ever_logged_or_put_into_a_message()
    {
        var failures = new List<QuoteResult>();
        foreach (var respond in new Func<HttpResponseMessage>[]
        {
            () => StubBroker.Json(HttpStatusCode.Unauthorized, $$"""{"errorCode":"DH-901","errorMessage":"{{TokenMarker}} {{ClientMarker}}"}"""),
            () => StubBroker.Json(HttpStatusCode.TooManyRequests, $$"""{"errorMessage":"{{TokenMarker}}"}"""),
            () => StubBroker.Json(HttpStatusCode.InternalServerError, $$"""{"errorCode":"DH-905","errorMessage":"{{ClientMarker}}"}"""),
            () => StubBroker.Json(HttpStatusCode.OK, $$"""{"unexpected":"{{TokenMarker}}"}"""),
            () => throw new HttpRequestException(HttpRequestError.Unknown, "failed for " + TokenMarker + " " + ClientMarker),
        })
        {
            _broker.Respond = _ => respond();
            failures.Add(await QuoteAsync("1"));
        }

        _log.Messages.ShouldNotBeEmpty();
        var everything = string.Join('\n', _log.Messages.Concat(failures.Select(f => f.Failure!.Message)));
        everything.ShouldNotContain(TokenMarker);
        everything.ShouldNotContain(ClientMarker);
        everything.ShouldNotContain("api.dhan.co");
    }

    [Fact]
    public void The_quote_client_has_a_15_second_timeout_and_accepts_answers_up_to_4_MB()
    {
        using var client = _broker.CreateClient(BrokerHttpClientNames.DhanQuote);

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(15));
        client.MaxResponseContentBufferSize.ShouldBe(4 * 1024 * 1024);
    }

    [Fact]
    public void The_throttle_allows_one_request_a_second()
    {
        QuoteThrottle.RequestsPerSecond.ShouldBe(1);
    }

    private sealed class FakeCredentials : IDataAccountCredentials
    {
        public DataAccountCredentials? Value { get; set; } = new(SecretValue.TryCreate("token-" + TokenMarker, out var token, out _) ? token : throw new InvalidOperationException(), ClientMarker);

        public ValueTask<DataAccountCredentials?> GetCredentialsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Value);
    }

    private sealed class CapturingLogger : ILogger<DhanQuoteClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
