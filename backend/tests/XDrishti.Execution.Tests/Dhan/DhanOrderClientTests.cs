using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests.Dhan;

public sealed class DhanOrderClientTests
{
    private const string Token = "SECRET-TOKEN-VALUE";
    private static readonly string Cid = ClientOrderId.Create(new DateOnly(2026, 10, 26), 361, LegKind.Entry, 1).Value.ToString();
    private static readonly BrokerLeg Entry = new(Cid, LegKind.Entry, ExecSide.Buy, "1594", 10, 1500.00m);

    private sealed class Creds(DhanCredentials? value) : IExecCredentials
    {
        public DhanCredentials? Current() => value;
    }

    private sealed record Seen(HttpMethod Method, Uri Uri, string? Body, string? Token);

    private sealed class Script(params Func<Seen, HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        private int _i;

        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var seen = new Seen(request.Method, request.RequestUri!, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken), request.Headers.TryGetValues("access-token", out var v) ? v.Single() : null);
            Requests.Add(seen);
            return steps[Math.Min(_i++, steps.Length - 1)](seen);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body) };

    private static HttpResponseMessage Fault(HttpRequestError error) => throw new HttpRequestException(error, "boom " + Token);

    private static (DhanOrderClient Client, Script Handler, FakeTimeProvider Clock) Make(Script handler, DhanCredentials? creds = null, bool noCreds = false)
    {
        var clock = new FakeTimeProvider();
        var http = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, handler)) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new DhanOrderClient(http, new Creds(noCreds ? null : creds ?? new DhanCredentials("1000000001", Token)), ExecEnvironment.Sandbox, NullLogger<DhanOrderClient>.Instance, clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        return (client, handler, clock);
    }

    [Fact]
    public async Task A_place_maps_to_the_documented_body_and_header()
    {
        var (client, handler, _) = Make(new Script(_ => Json(HttpStatusCode.OK, """{"orderId":"112111182198","orderStatus":"PENDING"}""")));

        var ack = await client.PlaceAsync(Entry, CancellationToken.None);

        ack.Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        ack.BrokerOrderId.ShouldBe("112111182198");
        var seen = handler.Requests.Single();
        seen.Method.ShouldBe(HttpMethod.Post);
        seen.Uri.ToString().ShouldBe("https://sandbox.dhan.co/v2/orders");
        seen.Token.ShouldBe(Token);
        using var doc = JsonDocument.Parse(seen.Body!);
        var r = doc.RootElement;
        r.GetProperty("dhanClientId").GetString().ShouldBe("1000000001");
        r.GetProperty("correlationId").GetString().ShouldBe("XD2610260000A1E1");
        r.GetProperty("transactionType").GetString().ShouldBe("BUY");
        r.GetProperty("exchangeSegment").GetString().ShouldBe("NSE_EQ");
        r.GetProperty("productType").GetString().ShouldBe("INTRADAY");
        r.GetProperty("orderType").GetString().ShouldBe("LIMIT");
        r.GetProperty("validity").GetString().ShouldBe("DAY");
        r.GetProperty("securityId").GetString().ShouldBe("1594");
        r.GetProperty("quantity").GetInt32().ShouldBe(10);
        r.GetProperty("price").GetDecimal().ShouldBe(1500.00m);
        r.EnumerateObject().Select(p => p.Name).ShouldBe(["dhanClientId", "correlationId", "transactionType", "exchangeSegment", "productType", "orderType", "validity", "securityId", "quantity", "price"]);
    }

    [Theory]
    [InlineData(0, "1500.00", "1594", LegKind.Entry)]
    [InlineData(10, "1500.03", "1594", LegKind.Entry)]
    [InlineData(10, "0", "1594", LegKind.Entry)]
    [InlineData(10, "1500.00", "15x4", LegKind.Entry)]
    [InlineData(10, "1500.00", "1594", LegKind.Stop)]
    [InlineData(10, "1500.00", "1594", LegKind.Target)]
    public async Task An_invalid_instruction_is_refused_before_any_request(int qty, string price, string security, LegKind kind)
    {
        var (client, handler, _) = Make(new Script(_ => Json(HttpStatusCode.OK, "{}")));
        var leg = Entry with { Qty = qty, Price = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), SecurityId = security, Kind = kind };

        var ack = await client.PlaceAsync(leg, CancellationToken.None);

        ack.Outcome.ShouldBe(BrokerOutcome.NotSent);
        ack.Reason!.ShouldStartWith("execution.instruction_invalid");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_null_security_id_is_a_typed_refusal_not_an_exception()
    {
        var (client, handler, _) = Make(new Script(_ => Json(HttpStatusCode.OK, "{}")));

        var ack = await client.PlaceAsync(Entry with { SecurityId = null! }, CancellationToken.None);

        ack.Reason.ShouldBe("execution.instruction_invalid:security_id");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bad_client_order_id_is_refused_before_any_request()
    {
        var (client, handler, _) = Make(new Script(_ => Json(HttpStatusCode.OK, "{}")));

        (await client.PlaceAsync(Entry with { ClientOrderId = "free-text" }, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.NotSent);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_credentials_refuse_locally_with_no_request()
    {
        var (client, handler, _) = Make(new Script(_ => Json(HttpStatusCode.OK, "{}")), noCreds: true);

        (await client.PlaceAsync(Entry, CancellationToken.None)).Reason.ShouldBe("execution.token_unavailable");
        (await client.CancelAsync(Cid, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.NotSent);
        await Should.ThrowAsync<BrokerLookupFailedException>(() => client.LookupAsync(Cid, CancellationToken.None));
        handler.Requests.ShouldBeEmpty();
    }

    public static TheoryData<int, string, BrokerOutcome, string?> Outcomes => new()
    {
        { 200, """{"orderId":"1","orderStatus":"TRANSIT"}""", BrokerOutcome.Acknowledged, "TRANSIT" },
        { 200, """{"orderId":"1","orderStatus":"REJECTED"}""", BrokerOutcome.NotSent, "broker_rejected" },
        { 200, """{"orderId":"","orderStatus":"PENDING"}""", BrokerOutcome.Unknown, "ack_unreadable" },
        { 200, """{"orderStatus":"PENDING"}""", BrokerOutcome.Unknown, "ack_unreadable" },
        { 200, """{"orderId":"1","orderStatus":"TRADED"}""", BrokerOutcome.Unknown, "ack_unreadable" },
        { 200, "not json", BrokerOutcome.Unknown, "ack_unreadable" },
        { 400, """{"errorType":"Order_Error","errorCode":"DH-906","errorMessage":"bad"}""", BrokerOutcome.NotSent, "broker_rejected" },
        { 401, """{"errorType":"Invalid_Authentication","errorCode":"DH-901","errorMessage":"token"}""", BrokerOutcome.NotSent, "token_invalid" },
        { 429, """{"errorType":"Rate_Limit","errorCode":"DH-904","errorMessage":"slow"}""", BrokerOutcome.NotSent, "rate_limited_by_broker" },
        { 400, """{"errorType":"Input_Exception","errorCode":"DH-905","errorMessage":"Invalid IP address"}""", BrokerOutcome.NotSent, "ip_not_allowed" },
        { 400, """{"errorType":"Input_Exception","errorCode":"DH-905","errorMessage":"missing field"}""", BrokerOutcome.NotSent, "input_exception" },
        { 400, """{"errorType":"Internal_Server_Error","errorCode":"DH-908","errorMessage":"x"}""", BrokerOutcome.NotSent, "broker_error" },
        { 429, "", BrokerOutcome.Unknown, "unreadable_or_server_error" },
        { 400, "<html>", BrokerOutcome.Unknown, "unreadable_or_server_error" },
        { 400, """{"errorCode":"DH-906"}""", BrokerOutcome.Unknown, "unreadable_or_server_error" },
        { 500, """{"errorType":"x","errorCode":"DH-908","errorMessage":"y"}""", BrokerOutcome.Unknown, "unreadable_or_server_error" },
        { 302, "", BrokerOutcome.Unknown, "unreadable_or_server_error" },
    };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task The_three_way_outcome_table(int status, string body, BrokerOutcome outcome, string? reason)
    {
        var (client, handler, _) = Make(new Script(_ => Json((HttpStatusCode)status, body)));

        var ack = await client.PlaceAsync(Entry, CancellationToken.None);

        ack.Outcome.ShouldBe(outcome);
        ack.Reason.ShouldBe(reason);
        handler.Requests.Count.ShouldBe(1, "a write is never retried");
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, BrokerOutcome.Unknown)]
    [InlineData(HttpRequestError.ProxyTunnelError, BrokerOutcome.Unknown)]
    [InlineData(HttpRequestError.NameResolutionError, BrokerOutcome.NotSent)]
    [InlineData(HttpRequestError.SecureConnectionError, BrokerOutcome.NotSent)]
    [InlineData(HttpRequestError.ResponseEnded, BrokerOutcome.Unknown)]
    [InlineData(HttpRequestError.Unknown, BrokerOutcome.Unknown)]
    public async Task A_transport_fault_is_not_sent_only_before_the_connection_and_the_message_never_leaks(HttpRequestError error, BrokerOutcome outcome)
    {
        var (client, handler, _) = Make(new Script(_ => Fault(error)));

        var ack = await client.PlaceAsync(Entry, CancellationToken.None);

        ack.Outcome.ShouldBe(outcome);
        (ack.Reason ?? string.Empty).ShouldNotContain(Token);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_timeout_after_sending_is_unknown_and_is_not_retried()
    {
        var clock = new FakeTimeProvider();
        var hang = new HangHandler();
        var http = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, hang)) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new DhanOrderClient(http, new Creds(new DhanCredentials("1", Token)), ExecEnvironment.Sandbox, NullLogger<DhanOrderClient>.Instance, clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        var task = client.PlaceAsync(Entry, CancellationToken.None);
        await hang.Started.Task;
        clock.Advance(TimeSpan.FromSeconds(11));
        var ack = await task;

        ack.Outcome.ShouldBe(BrokerOutcome.Unknown);
        hang.Calls.ShouldBe(1);
    }

    private sealed class HangHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new();

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    [Fact]
    public async Task A_lookup_finds_an_order_by_client_id_and_reads_empty_as_absent()
    {
        var found = """{"orderId":"77","correlationId":"XD2610260000A1E1","orderStatus":"PENDING","transactionType":"BUY","securityId":"1594","quantity":10,"price":1500.0}""";
        var (client, handler, _) = Make(new Script(_ => Json(HttpStatusCode.OK, found)));

        var leg = await client.LookupAsync(Cid, CancellationToken.None);

        leg.ShouldNotBeNull();
        (leg.Side, leg.Qty, leg.Price, leg.SecurityId, leg.Kind).ShouldBe((ExecSide.Buy, 10, 1500.0m, "1594", LegKind.Entry));
        handler.Requests.Single().Uri.ToString().ShouldBe("https://sandbox.dhan.co/v2/orders/external/" + Cid);

        foreach (var empty in new[] { "[]", "{}" })
        {
            var (c2, _, _) = Make(new Script(_ => Json(HttpStatusCode.OK, empty)));
            (await c2.LookupAsync(Cid, CancellationToken.None)).ShouldBeNull();
        }

        var (c3, _, _) = Make(new Script(_ => Json(HttpStatusCode.NotFound, """{"errorType":"Order_Error","errorCode":"DH-906","errorMessage":"Order not found"}""")));
        (await c3.LookupAsync(Cid, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(404, "")]
    [InlineData(404, "<html>no route</html>")]
    [InlineData(404, """{"errorType":"x","errorCode":"DH-908","errorMessage":"route"}""")]
    [InlineData(200, """{"orderId":"1","correlationId":"XD2610260000A1E2","transactionType":"BUY","securityId":"1594","quantity":10,"price":1500}""")]
    [InlineData(200, """{"orderId":"1","transactionType":"BUY","securityId":"1594","quantity":10,"price":1500}""")]
    [InlineData(500, "")]
    [InlineData(200, "not json")]
    [InlineData(200, """{"orderId":"1"}""")]
    public async Task A_lookup_that_cannot_be_read_is_a_failure_never_absence_and_a_read_is_retried_once(int status, string body)
    {
        var (client, handler, clock) = Make(new Script(_ => Json((HttpStatusCode)status, body)));

        var task = client.LookupAsync(Cid, CancellationToken.None);
        while (!task.IsCompleted)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Yield();
        }

        await Should.ThrowAsync<BrokerLookupFailedException>(() => task);
        handler.Requests.Count.ShouldBe(status == 500 ? 2 : 1);
    }

    [Fact]
    public async Task Modify_and_cancel_look_up_the_broker_id_first_then_write_once()
    {
        var found = """{"orderId":"77","correlationId":"XD2610260000A1E1","orderStatus":"PENDING","transactionType":"BUY","securityId":"1594","quantity":10,"price":1500.0}""";
        var (client, handler, _) = Make(new Script(
            _ => Json(HttpStatusCode.OK, found),
            _ => Json(HttpStatusCode.OK, """{"orderId":"77","orderStatus":"PENDING"}"""),
            _ => Json(HttpStatusCode.OK, found),
            _ => Json(HttpStatusCode.OK, """{"orderId":"77","orderStatus":"CANCELLED"}""")));

        (await client.ModifyAsync(Cid, 1499.95m, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        (await client.CancelAsync(Cid, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);

        handler.Requests.Select(r => $"{r.Method} {r.Uri.AbsolutePath}").ShouldBe(["GET /v2/orders/external/" + Cid, "PUT /v2/orders/77", "GET /v2/orders/external/" + Cid, "DELETE /v2/orders/77"]);
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        body.RootElement.GetProperty("orderId").GetString().ShouldBe("77");
        body.RootElement.GetProperty("price").GetDecimal().ShouldBe(1499.95m);
        (await client.ModifyAsync(Cid, 1499.97m, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.NotSent);
    }

    [Fact]
    public void Credentials_print_redacted_and_the_adapter_cannot_be_built_for_live_or_none()
    {
        new DhanCredentials("1000000001", Token).ToString().ShouldNotContain(Token);
        foreach (var env in new[] { ExecEnvironment.Live, ExecEnvironment.None })
        {
            Should.Throw<InvalidOperationException>(() => new DhanOrderClient(new HttpClient(), new Creds(null), env, NullLogger<DhanOrderClient>.Instance, TimeProvider.System, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        }
    }

    [Fact]
    public async Task The_token_is_never_logged()
    {
        var sink = new ListLogger();
        var http = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, new Script(_ => Json(HttpStatusCode.OK, """{"orderId":"1","orderStatus":"PENDING"}""")))) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new DhanOrderClient(http, new Creds(new DhanCredentials("1000000001", Token)), ExecEnvironment.Sandbox, sink, TimeProvider.System, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        await client.PlaceAsync(Entry, CancellationToken.None);

        sink.Lines.ShouldNotBeEmpty();
        sink.Lines.ShouldNotContain(l => l.Contains(Token, StringComparison.Ordinal) || l.Contains("1000000001", StringComparison.Ordinal));
    }

    private sealed class ListLogger : ILogger<DhanOrderClient>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
