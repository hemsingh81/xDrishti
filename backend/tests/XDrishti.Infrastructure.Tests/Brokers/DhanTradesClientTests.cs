using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>What the trade history client sends to Dhan and how it reads the answers (ADR 0025, P2-13 criteria 1, 2, 4; made-up values, the token carries a marker that must appear nowhere but the header).</summary>
public sealed class DhanTradesClientTests : IDisposable
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    private static readonly DateOnly From = new(2026, 9, 1);

    private static readonly DateOnly To = new(2026, 9, 30);

    private readonly StubBroker _broker = new();

    private readonly CapturingLogger _log = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private static SecretValue Token => SecretValue.TryCreate("token-" + Marker, out var value, out _) ? value : throw new InvalidOperationException();

    private DhanTradesClient Client => new(_broker, new DhanHistoryThrottle(Options.Create(new BrokerHistoryOptions { CallSpacingMs = 0 }), TimeProvider.System), _log);

    [Fact]
    public async Task It_asks_for_one_page_with_the_dates_in_the_path_the_token_in_the_header_and_nothing_else()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[]");

        var result = await Client.FetchPageAsync(Token, From, To, 3, Ct);

        result.IsSuccess.ShouldBeTrue();
        var request = _broker.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/3");
        request.Uri.Query.ShouldBeEmpty();
        request.Headers["access-token"].ShouldBe("token-" + Marker);
        request.Body.ShouldBeNull();
        request.Headers.Keys.Where(k => k.Contains("secret", StringComparison.OrdinalIgnoreCase) || k.StartsWith("app_", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Pages_count_from_zero()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[]");

        await Client.FetchPageAsync(Token, From, To, 0, Ct);

        _broker.Requests.Single().Uri.AbsolutePath.ShouldBe("/v2/trades/2026-09-01/2026-09-30/0");
    }

    [Fact]
    public async Task The_sample_answer_is_read_into_trades_and_counts()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, DhanSamples.Text("dhan-trades-page.json"));

        var page = (await Client.FetchPageAsync(Token, From, To, 0, Ct)).Value;

        (page.Rows, page.Trades.Count, page.Dropped, page.Skipped.Values.Sum()).ShouldBe((7, 3, 1, 3));
    }

    [Fact]
    public async Task An_object_that_holds_the_rows_under_data_is_read_too()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "{\"data\":" + DhanSamples.Text("dhan-trades-page.json") + "}");

        (await Client.FetchPageAsync(Token, From, To, 0, Ct)).Value.Trades.Count.ShouldBe(3);
    }

    [Fact]
    public async Task An_empty_list_is_the_end_of_the_history()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[]");

        var page = (await Client.FetchPageAsync(Token, From, To, 1, Ct)).Value;

        page.Rows.ShouldBe(0);
        page.Trades.ShouldBeEmpty();
        page.NoData.ShouldBeFalse(); // a real empty list, not Dhan's "no data" code
    }

    [Fact]
    public async Task Dhans_no_data_error_is_an_empty_page_not_a_failure()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, """{"errorType":"Data_API_Error","errorCode":"DH-907","errorMessage":"No data present"}""");

        var result = await Client.FetchPageAsync(Token, From, To, 0, Ct);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Rows, result.Value.NoData).ShouldBe((0, true));
    }

    [Theory]
    [InlineData("""{"errorCode":"DH-907"}""")]
    [InlineData("""{"status":"failure","errorType":"Data_API_Error","errorCode":"DH-907","errorMessage":"No data present"}""")]
    public async Task Dhans_no_data_error_in_the_body_of_a_200_means_the_same_as_with_a_400(string body)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, body);

        var result = await Client.FetchPageAsync(Token, From, To, 0, Ct);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Rows, result.Value.Trades, result.Value.NoData).ShouldBe((0, [], true));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorCode":"DH-907"}""", BrokerFailureKind.Unauthorized, "DH-907")] // a refused token is never "no data"
    [InlineData(HttpStatusCode.InternalServerError, """{"errorCode":"DH-907"}""", BrokerFailureKind.Unavailable, "DH-907")]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"expired"}""", BrokerFailureKind.Unauthorized, "DH-901")]
    [InlineData(HttpStatusCode.Forbidden, "", BrokerFailureKind.Unauthorized, null)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"errorCode":"DH-904"}""", BrokerFailureKind.Rejected, "DH-904")]
    [InlineData(HttpStatusCode.TooManyRequests, "", BrokerFailureKind.Rejected, "DH-904")] // a 429 without a code in its body is the rate limit all the same (ADR 0027)
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-905"}""", BrokerFailureKind.Rejected, "DH-905")]
    [InlineData(HttpStatusCode.InternalServerError, """{"errorCode":"DH-908"}""", BrokerFailureKind.Unavailable, "DH-908")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.Found, "", BrokerFailureKind.Unavailable, null)] // redirects are not followed
    [InlineData(HttpStatusCode.OK, """{"status":"failure","errorCode":"DH-905"}""", BrokerFailureKind.Rejected, "DH-905")]
    [InlineData(HttpStatusCode.OK, "not json", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.OK, "\"text\"", BrokerFailureKind.Unavailable, null)]
    public async Task Failures_are_mapped_with_the_broker_code_only_and_never_become_no_data(HttpStatusCode status, string body, BrokerFailureKind kind, string? code)
    {
        _broker.Respond = _ => StubBroker.Json(status, body);

        var result = await Client.FetchPageAsync(Token, From, To, 0, Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.ShouldBe(new BrokerFailure(kind, code));
    }

    [Fact]
    public async Task A_timeout_or_network_failure_is_unavailable_and_a_cancellation_is_not_hidden()
    {
        _broker.Respond = _ => throw new TaskCanceledException("timed out");
        var timedOut = await Client.FetchPageAsync(Token, From, To, 0, Ct);
        _broker.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0");
        var unreachable = await Client.FetchPageAsync(Token, From, To, 0, Ct);

        timedOut.Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
        unreachable.Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _broker.Respond = _ => throw new TaskCanceledException();
        await Should.ThrowAsync<OperationCanceledException>(async () => await Client.FetchPageAsync(Token, From, To, 0, cancelled.Token));
    }

    [Fact]
    public async Task A_page_with_more_rows_than_any_page_has_is_unusable()
    {
        var rows = string.Join(',', Enumerable.Range(1, DhanTradesClient.MaxRows + 1).Select(i => $$"""{"exchangeTradeId":"{{i}}"}"""));
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[" + rows + "]");

        (await Client.FetchPageAsync(Token, From, To, 0, Ct)).Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
    }

    [Fact]
    public async Task An_answer_over_5_MB_is_refused_as_unavailable()
    {
        var padding = new string('x', (5 * 1024 * 1024) + 1024);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, $$"""[{"exchangeTradeId":"T1","tradingSymbol":"{{padding}}"}]""");

        var result = await Client.FetchPageAsync(Token, From, To, 0, Ct);

        result.Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
    }

    [Fact]
    public void The_history_client_has_a_30_second_timeout_and_accepts_answers_up_to_5_MB()
    {
        using var client = _broker.CreateClient(BrokerHttpClientNames.DhanHistory);

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        client.MaxResponseContentBufferSize.ShouldBe(5 * 1024 * 1024);
    }

    [Fact]
    public async Task The_token_the_url_and_the_rows_are_never_logged()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.Unauthorized, $$"""{"errorCode":"DH-901","errorMessage":"token-{{Marker}} is invalid","holder":"Example Holder"}""");
        await Client.FetchPageAsync(Token, From, To, 0, Ct);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, DhanSamples.Text("dhan-trades-page.json"));
        await Client.FetchPageAsync(Token, From, To, 0, Ct);

        var logged = string.Join('\n', _log.Messages);
        logged.ShouldContain("DH-901");
        logged.ShouldNotContain(Marker);
        logged.ShouldNotContain("api.dhan.co");
        logged.ShouldNotContain("2026-09");
        logged.ShouldNotContain("Example Holder");
        logged.ShouldNotContain("EXMPL");
        logged.ShouldNotContain("2611250001");
    }

    [Fact]
    public async Task The_guard_refuses_every_other_shape_of_the_trade_path_for_the_history_client()
    {
        using var client = _broker.CreateClient(BrokerHttpClientNames.DhanHistory);

        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/trades/2026-09-01/2026-09-30"), Ct)); // no page
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0?x=1"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/trades/2026-13-01/2026-09-30/0"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/orders"), Ct));
        using var post = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0")) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.SendAsync(post, Ct));

        _broker.Requests.ShouldBeEmpty();
    }

    private sealed class CapturingLogger : ILogger<DhanTradesClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
