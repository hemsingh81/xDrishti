using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>What the two book clients send (one GET, header only, no query, no body) and how they read the answers (spec P11-01 criteria 12 and 13).</summary>
public sealed class DhanBookClientsTests : IDisposable
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    private readonly StubBroker _broker = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _broker.Dispose();

    private static SecretValue Token => SecretValue.TryCreate("token-" + Marker, out var value, out _) ? value : throw new InvalidOperationException();

    private static DhanHistoryThrottle Throttle => new(Options.Create(new BrokerHistoryOptions { CallSpacingMs = 0 }), TimeProvider.System);

    private DhanTradeBookClient Trades => new(_broker, Throttle, NullLogger<DhanTradeBookClient>.Instance);

    private DhanOrderBookClient Orders => new(_broker, Throttle, NullLogger<DhanOrderBookClient>.Instance);

    [Fact]
    public async Task The_trade_book_is_one_GET_of_the_exact_path_with_the_token_in_the_header_and_nothing_else()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, DhanSamples.Text("dhan-trade-book-day.json"));

        var read = (await Trades.ReadAsync(Token, Ct)).Value;

        read.Rows.Count.ShouldBe(3);
        var request = _broker.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/trades");
        request.Uri.Query.ShouldBeEmpty();
        request.Headers["access-token"].ShouldBe("token-" + Marker);
        request.Body.ShouldBeNull();
    }

    [Fact]
    public async Task The_order_book_is_one_GET_of_the_exact_path_with_no_id_no_query_and_no_body()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, DhanSamples.Text("dhan-order-book-day.json"));

        var read = (await Orders.ReadAsync(Token, Ct)).Value;

        read.Rows.Count.ShouldBe(5);
        var request = _broker.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.AbsoluteUri.ShouldBe("https://api.dhan.co/v2/orders");
        request.Body.ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"errorCode":"DH-901"}""", BrokerFailureKind.Unauthorized)] // token_expired, never an empty day
    [InlineData(HttpStatusCode.TooManyRequests, "", BrokerFailureKind.RateLimited)] // a quiet retry
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"DH-904"}""", BrokerFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, "", BrokerFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.Found, "", BrokerFailureKind.Unavailable)] // redirects are not followed
    public async Task A_refused_token_a_rate_limit_or_an_outage_is_a_failure_and_never_an_empty_day(HttpStatusCode status, string body, BrokerFailureKind kind)
    {
        _broker.Respond = _ => StubBroker.Json(status, body);

        (await Trades.ReadAsync(Token, Ct)).Failure!.Kind.ShouldBe(kind);
        (await Orders.ReadAsync(Token, Ct)).Failure!.Kind.ShouldBe(kind);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"errorCode":"DH-907"}""")]
    public async Task An_empty_day_is_an_empty_read(string body)
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.OK, body);

        var read = (await Trades.ReadAsync(Token, Ct)).Value;

        read.Seen.ShouldBe(0);
        read.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dhans_no_data_error_with_a_400_is_an_empty_read_too()
    {
        _broker.Respond = _ => StubBroker.Json(HttpStatusCode.BadRequest, """{"errorCode":"DH-907"}""");

        (await Orders.ReadAsync(Token, Ct)).IsSuccess.ShouldBeTrue();
    }
}
