using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>What the holdings client sends to Dhan and how it reads the answers (made-up values; the token carries a marker that must appear nowhere but the header).</summary>
public sealed class DhanHoldingsClientTests : IDisposable
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    private readonly StubBroker _broker = new();

    private readonly CapturingLogger _log = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private static SecretValue Token => SecretValue.TryCreate("token-" + Marker, out var value, out _) ? value : throw new InvalidOperationException();

    private DhanHoldingsClient Client => new(_broker, _log);

    /// <summary>The documented example of GET /v2/holdings (docs read 2026-10-11), with made-up values.</summary>
    private const string Documented = """
        [
          {"exchange":"ALL","tradingSymbol":"EXMPL","securityId":"1001","isin":"INE000A01011","totalQty":100,"dpQty":100,"t1Qty":0,"mtf_t1_qty":0,"mtf_qty":0,"availableQty":100,"collateralQty":0,"avgCostPrice":50.55,"lastTradedPrice":60.1},
          {"exchange":"BSE","tradingSymbol":"OTHER","securityId":"500112","isin":"INE000B01012","totalQty":7,"avgCostPrice":"1000","lastTradedPrice":"1100.5"}
        ]
        """;

    [Fact]
    public async Task It_asks_for_the_holdings_with_the_token_in_the_access_token_header_and_nothing_else()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Documented);

        var result = await Client.FetchAsync(Token, Ct);

        result.IsSuccess.ShouldBeTrue();
        var request = _broker.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/holdings");
        request.Uri.Query.ShouldBeEmpty();
        request.Headers["access-token"].ShouldBe("token-" + Marker);
        request.Body.ShouldBeNull();
        request.Headers.Keys.Where(k => k.Contains("secret", StringComparison.OrdinalIgnoreCase) || k.StartsWith("app_", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_documented_answer_is_read_into_holdings()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Documented);

        var rows = (await Client.FetchAsync(Token, Ct)).Value;

        rows.Count.ShouldBe(2);
        var first = rows[0];
        (first.Exchange, first.SecurityId, first.Isin, first.TradingSymbol, first.Qty, first.AvgCost, first.LastPrice)
            .ShouldBe(("ALL", "1001", "INE000A01011", "EXMPL", 100L, 50.55m, 60.1m));
        var second = rows[1];
        (second.Exchange, second.Qty, second.AvgCost, second.LastPrice).ShouldBe(("BSE", 7L, 1000m, 1100.5m)); // numbers sent as text are read too
    }

    [Fact]
    public async Task Rows_without_a_symbol_or_security_id_are_dropped_and_missing_fields_default()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, """
            [{"tradingSymbol":"NOID","totalQty":5},{"securityId":"7","totalQty":5},{"tradingSymbol":"PLAIN","securityId":"8","totalQty":3.0,"avgCostPrice":"n/a"}]
            """);

        var rows = (await Client.FetchAsync(Token, Ct)).Value;

        var row = rows.ShouldHaveSingleItem();
        (row.Exchange, row.Isin, row.Qty, row.AvgCost, row.LastPrice, row.TradingSymbol).ShouldBe(("ALL", (string?)null, 3L, 0m, 0m, "PLAIN"));
    }

    [Fact]
    public async Task Absurd_numbers_read_as_missing_instead_of_breaking_the_sync()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, """
            [{"tradingSymbol":"BIG","securityId":"1","totalQty":1e40,"avgCostPrice":5},{"tradingSymbol":"OK","securityId":"2","totalQty":3,"avgCostPrice":1e30,"lastTradedPrice":-7e20}]
            """);

        var rows = (await Client.FetchAsync(Token, Ct)).Value;

        rows.Select(r => (r.TradingSymbol, r.Qty, r.AvgCost, r.LastPrice)).ShouldBe([("BIG", 0L, 5m, 0m), ("OK", 3L, 0m, 0m)]);
    }

    [Fact]
    public async Task An_empty_list_is_an_account_without_holdings()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[]");

        (await Client.FetchAsync(Token, Ct)).Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("DH-907")] // documented: "incorrect parameters or no data present"; the holdings request has no parameters
    [InlineData("DH-1111")] // not documented by Dhan, tolerated as an alias
    public async Task Dhans_no_data_error_is_an_empty_list_not_a_failure(string code)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, $$"""{"errorType":"Data_API_Error","errorCode":"{{code}}","errorMessage":"No data present"}""");

        var result = await Client.FetchAsync(Token, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_failure_carries_no_text_of_the_answer_only_a_well_formed_code(HttpStatusCode status)
    {
        const string Upstream = "UPSTREAM-BODY-MARKER";
        _broker.Respond = _ => StubBroker.Json(status, $$"""{"errorType":"{{Upstream}}","errorCode":"{{Upstream}}","errorMessage":"{{Upstream}}"}""");

        var result = await Client.FetchAsync(Token, Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Failure!.BrokerCode.ShouldBeNull();
        result.Failure.ToString().ShouldNotContain(Upstream);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, BrokerFailureKind.Unauthorized)] // a refused token is never "no data"
    [InlineData(HttpStatusCode.InternalServerError, BrokerFailureKind.Unavailable)]
    public async Task The_no_data_code_is_only_an_empty_list_for_a_4xx_answer_that_is_not_a_refused_token(HttpStatusCode status, BrokerFailureKind kind)
    {
        _broker.Respond = _ => StubBroker.Json(status, """{"errorCode":"DH-907"}""");

        var result = await Client.FetchAsync(Token, Ct);

        result.Failure.ShouldBe(new BrokerFailure(kind, "DH-907"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"Client ID or user generated access token is invalid or expired."}""", BrokerFailureKind.Unauthorized, "DH-901")]
    [InlineData(HttpStatusCode.Forbidden, """{"errorType":"Invalid_Access","errorCode":"DH-902","errorMessage":"x"}""", BrokerFailureKind.Unauthorized, "DH-902")]
    [InlineData(HttpStatusCode.Unauthorized, "", BrokerFailureKind.Unauthorized, null)]
    [InlineData(HttpStatusCode.BadRequest, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"expired"}""", BrokerFailureKind.Unauthorized, "DH-901")] // the code counts, whatever the status
    [InlineData(HttpStatusCode.NotFound, """{"errorCode":"DH-901"}""", BrokerFailureKind.Unauthorized, "DH-901")]
    [InlineData(HttpStatusCode.InternalServerError, """{"errorCode":"DH-901"}""", BrokerFailureKind.Unavailable, "DH-901")]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-905"}""", BrokerFailureKind.Rejected, "DH-905")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"errorCode":"DH-904"}""", BrokerFailureKind.RateLimited, "DH-904")]
    [InlineData(HttpStatusCode.InternalServerError, """{"errorCode":"DH-908"}""", BrokerFailureKind.Unavailable, "DH-908")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.Found, "", BrokerFailureKind.Unavailable, null)] // redirects are not followed
    [InlineData(HttpStatusCode.OK, """{"status":"failure","errorCode":"DH-905"}""", BrokerFailureKind.Rejected, "DH-905")]
    [InlineData(HttpStatusCode.OK, """{"data":[]}""", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.OK, "not json", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"FAKE-SECRET-MARKER-DO-NOT-LEAK"}""", BrokerFailureKind.Rejected, null)] // an echo, not a Dhan code
    public async Task Failures_are_mapped_with_the_broker_code_only(HttpStatusCode status, string body, BrokerFailureKind kind, string? code)
    {
        _broker.Respond = _ => StubBroker.Json(status, body);

        var result = await Client.FetchAsync(Token, Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.ShouldBe(new BrokerFailure(kind, code));
    }

    [Fact]
    public async Task A_timeout_or_network_failure_is_unavailable_and_a_cancellation_is_not_hidden()
    {
        _broker.Respond = _ => throw new TaskCanceledException("timed out");
        var timedOut = await Client.FetchAsync(Token, Ct);
        _broker.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused https://api.dhan.co/v2/holdings");
        var unreachable = await Client.FetchAsync(Token, Ct);

        timedOut.Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
        unreachable.Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _broker.Respond = _ => throw new TaskCanceledException();
        await Should.ThrowAsync<OperationCanceledException>(async () => await Client.FetchAsync(Token, cancelled.Token));
    }

    [Fact]
    public async Task An_answer_with_more_rows_than_any_account_has_is_unusable()
    {
        var rows = string.Join(',', Enumerable.Range(1, DhanHoldingsClient.MaxRows + 1).Select(i => $$"""{"tradingSymbol":"S{{i}}","securityId":"{{i}}","totalQty":1}"""));
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[" + rows + "]");

        (await Client.FetchAsync(Token, Ct)).Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
    }

    [Fact]
    public void The_holdings_client_has_a_30_second_timeout_and_accepts_answers_up_to_5_MB()
    {
        using var client = _broker.CreateClient(BrokerHttpClientNames.DhanHoldings);
        using var consent = _broker.CreateClient(BrokerHttpClientNames.Dhan);

        client.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        client.MaxResponseContentBufferSize.ShouldBe(5 * 1024 * 1024);
        consent.Timeout.ShouldBe(TimeSpan.FromSeconds(15)); // the consent flow keeps its small limits
        consent.MaxResponseContentBufferSize.ShouldBe(256 * 1024);
    }

    [Fact]
    public async Task The_token_the_url_and_the_answer_are_never_logged()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.Unauthorized, $$"""{"errorCode":"DH-901","errorMessage":"token-{{Marker}} is invalid","holder":"Example Holder"}""");

        await Client.FetchAsync(Token, Ct);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, Documented);
        await Client.FetchAsync(Token, Ct);

        var logged = string.Join('\n', _log.Messages);
        logged.ShouldContain("DH-901");
        logged.ShouldNotContain(Marker);
        logged.ShouldNotContain("api.dhan.co");
        logged.ShouldNotContain("Example Holder");
        logged.ShouldNotContain("EXMPL");
    }

    [Fact]
    public async Task The_allow_list_guard_refuses_other_paths_even_for_the_holdings_client()
    {
        using var client = _broker.CreateClient(BrokerHttpClientNames.DhanHoldings);

        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/positions"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/holdings?page=2"), Ct));
        using var post = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.dhan.co/v2/holdings")) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.SendAsync(post, Ct));

        _broker.Requests.ShouldBeEmpty();
    }

    private sealed class CapturingLogger : ILogger<DhanHoldingsClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
