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

/// <summary>What the ledger client sends to Dhan and how it reads the answers (ADR 0025, P2-13 criteria 1, 3, 4).</summary>
public sealed class DhanLedgerClientTests : IDisposable
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    private static readonly DateOnly From = new(2026, 9, 1);

    private static readonly DateOnly To = new(2026, 9, 30);

    private readonly StubBroker _broker = new();

    private readonly CapturingLogger _log = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private static SecretValue Token => SecretValue.TryCreate("token-" + Marker, out var value, out _) ? value : throw new InvalidOperationException();

    private DhanLedgerClient Client => new(_broker, new DhanHistoryThrottle(Options.Create(new BrokerHistoryOptions { CallSpacingMs = 0 }), TimeProvider.System), _log);

    [Fact]
    public async Task It_asks_for_the_period_with_exactly_two_query_keys_and_the_token_in_the_header()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[]");

        var result = await Client.FetchAsync(Token, From, To, Ct);

        result.IsSuccess.ShouldBeTrue();
        var request = _broker.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.AbsolutePath.ShouldBe("/v2/ledger");
        request.Uri.Query.ShouldBe("?from-date=2026-09-01&to-date=2026-09-30");
        request.Headers["access-token"].ShouldBe("token-" + Marker);
        request.Body.ShouldBeNull();
    }

    [Fact]
    public async Task The_sample_answer_is_read_into_lines()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, DhanSamples.Text("dhan-ledger.json"));

        var read = (await Client.FetchAsync(Token, From, To, Ct)).Value;

        (read.Received, read.Rows.Count, read.Dropped).ShouldBe((8, 4, 4));
    }

    [Fact]
    public async Task Dhans_no_data_error_is_an_empty_ledger()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, """{"errorCode":"DH-907"}""");

        var result = await Client.FetchAsync(Token, From, To, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldBeEmpty();
        result.Value.NoData.ShouldBeTrue();
    }

    [Fact]
    public async Task Dhans_no_data_error_in_the_body_of_a_200_means_the_same_as_with_a_400()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, """{"status":"failure","errorCode":"DH-907"}""");

        var result = await Client.FetchAsync(Token, From, To, Ct);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Rows, result.Value.NoData).ShouldBe(([], true));
    }

    [Fact]
    public async Task An_empty_list_is_an_empty_ledger_and_not_the_no_data_code()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[]");

        var result = await Client.FetchAsync(Token, From, To, Ct);

        (result.Value.Received, result.Value.NoData).ShouldBe((0, false));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorCode":"DH-907"}""", BrokerFailureKind.Unauthorized, "DH-907")]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorCode":"DH-901"}""", BrokerFailureKind.Unauthorized, "DH-901")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"errorCode":"DH-904"}""", BrokerFailureKind.Rejected, "DH-904")]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-905"}""", BrokerFailureKind.Rejected, "DH-905")]
    [InlineData(HttpStatusCode.InternalServerError, "", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.OK, "not json", BrokerFailureKind.Unavailable, null)]
    [InlineData(HttpStatusCode.OK, "42", BrokerFailureKind.Unavailable, null)]
    public async Task Failures_are_mapped_and_never_become_no_data(HttpStatusCode status, string body, BrokerFailureKind kind, string? code)
    {
        _broker.Respond = _ => StubBroker.Json(status, body);

        var result = await Client.FetchAsync(Token, From, To, Ct);

        result.Failure.ShouldBe(new BrokerFailure(kind, code));
    }

    [Fact]
    public async Task A_timeout_is_unavailable_and_a_cancellation_is_not_hidden()
    {
        _broker.Respond = _ => throw new TaskCanceledException("timed out");
        var timedOut = await Client.FetchAsync(Token, From, To, Ct);

        timedOut.Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => await Client.FetchAsync(Token, From, To, cancelled.Token));
    }

    [Fact]
    public async Task An_answer_with_more_lines_than_any_period_has_is_unusable()
    {
        var rows = string.Join(',', Enumerable.Range(1, DhanLedgerClient.MaxRows + 1).Select(_ => """{"voucherdate":"2026-09-01","credit":"1"}"""));
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, "[" + rows + "]");

        (await Client.FetchAsync(Token, From, To, Ct)).Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
    }

    [Fact]
    public async Task An_answer_over_5_MB_is_refused_as_unavailable()
    {
        var padding = new string('x', (5 * 1024 * 1024) + 1024);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, $$"""[{"voucherdate":"2026-09-01","credit":"1","narration":"{{padding}}"}]""");

        (await Client.FetchAsync(Token, From, To, Ct)).Failure.ShouldBe(new BrokerFailure(BrokerFailureKind.Unavailable));
    }

    [Fact]
    public async Task The_token_the_url_and_the_narrations_are_never_logged()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.Unauthorized, $$"""{"errorCode":"DH-901","errorMessage":"token-{{Marker}} is invalid"}""");
        await Client.FetchAsync(Token, From, To, Ct);
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, DhanSamples.Text("dhan-ledger.json"));
        await Client.FetchAsync(Token, From, To, Ct);

        var logged = string.Join('\n', _log.Messages);
        logged.ShouldNotContain(Marker);
        logged.ShouldNotContain("api.dhan.co");
        logged.ShouldNotContain("FUNDS");
        logged.ShouldNotContain("202600036701");
    }

    [Fact]
    public async Task The_guard_refuses_a_ledger_request_with_other_query_keys_or_dates_that_are_not_dates()
    {
        using var client = _broker.CreateClient(BrokerHttpClientNames.DhanHistory);

        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/ledger"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/ledger?from-date=2026-09-01"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30&x=1"), Ct));
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.GetAsync(new Uri("https://api.dhan.co/v2/ledger?from-date=today&to-date=2026-09-30"), Ct));
        using var post = new HttpRequestMessage(HttpMethod.Post, BrokerEndpoints.BuildLedgerUri(From, To)) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        await Should.ThrowAsync<BrokerEndpointNotAllowedException>(() => client.SendAsync(post, Ct));

        _broker.Requests.ShouldBeEmpty();
    }

    private sealed class CapturingLogger : ILogger<DhanLedgerClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
