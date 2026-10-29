using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests.Dhan;

/// <summary>The four read calls through the real client with a scripted handler (no network). Field names are Dhan's documented ones and UNVERIFIED against the sandbox.</summary>
public sealed class DhanReadsTests
{
    private sealed class Creds(bool present = true) : IExecCredentials
    {
        public DhanCredentials? Current() => present ? new DhanCredentials("1000000001", "T") : null;
    }

    private sealed class Script(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url)> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add((request.Method, request.RequestUri!.ToString()));
            return Task.FromResult(reply(request));
        }
    }

    private static (DhanOrderClient Client, Script Handler, FakeTimeProvider Clock) Make(Func<HttpRequestMessage, HttpResponseMessage> reply, bool creds = true)
    {
        var handler = new Script(reply);
        var http = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, handler)) { Timeout = Timeout.InfiniteTimeSpan };
        var clock = new FakeTimeProvider();
        return (new DhanOrderClient(http, new Creds(creds), ExecEnvironment.Sandbox, NullLogger<DhanOrderClient>.Instance, clock, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), handler, clock);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };

    [Fact]
    public async Task The_order_book_parses_rows_and_ignores_unknown_fields_and_the_case_of_names()
    {
        var (client, handler, _) = Make(_ => Json("""[{"orderId":"1","correlationId":"XD2610260000A1E1","orderStatus":"PENDING","transactionType":"BUY","securityId":"1594","quantity":10,"price":1500.05,"filled_qty":4,"extra":{"x":1}},{"OrderId":"2","orderStatus":"traded","securityId":"11536","quantity":"5","price":"20"}]"""));

        var book = await client.GetOrderBookAsync(CancellationToken.None);

        book.Readable.ShouldBeTrue();
        book.Value!.Count.ShouldBe(2);
        book.Value[0].ShouldBe(new BrokerOrderView("1", "XD2610260000A1E1", "PENDING", "1594", ExecSide.Buy, 10, 4, 1500.05m));
        book.Value[1].Status.ShouldBe("TRADED");
        handler.Seen.ShouldBe([(HttpMethod.Get, "https://sandbox.dhan.co/v2/orders")]);
    }

    [Fact]
    public async Task One_bad_row_makes_the_whole_book_unreadable_never_a_shorter_list()
    {
        var (client, _, _) = Make(_ => Json("""[{"orderId":"1","orderStatus":"PENDING","securityId":"1594","quantity":10,"price":1},{"orderId":"2"}]"""));

        var book = await client.GetOrderBookAsync(CancellationToken.None);

        (book.Readable, book.Value, book.Reason).ShouldBe((false, null, "row_unreadable"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task An_answer_that_is_not_a_list_is_unreadable(string body)
    {
        var (client, _, _) = Make(_ => Json(body));

        (await client.GetTradeBookAsync(CancellationToken.None)).Readable.ShouldBeFalse();
        (await client.GetPositionsAsync(CancellationToken.None)).Readable.ShouldBeFalse();
    }

    [Fact]
    public async Task Trades_positions_and_the_funds_quirk_are_read_by_name()
    {
        var (client, handler, _) = Make(r => Json(r.RequestUri!.AbsolutePath switch
        {
            "/v2/trades" => """[{"orderId":"1","securityId":"1594","tradedQuantity":3,"tradedPrice":1500.5,"transactionType":"SELL"}]""",
            "/v2/positions" => """[{"securityId":"1594","netQty":-3}]""",
            _ => """{"availabelBalance":98765.5,"sodLimit":1}""",
        }));

        (await client.GetTradeBookAsync(CancellationToken.None)).Value!.Single().ShouldBe(new BrokerTradeView("1", "1594", ExecSide.Sell, 3, 1500.5m));
        (await client.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(-3);
        (await client.GetFundsAsync(CancellationToken.None)).Value!.Available.ShouldBe(98765.5m);
        handler.Seen.Select(s => s.Method).ShouldAllBe(m => m == HttpMethod.Get);
    }

    [Fact]
    public async Task A_failing_read_is_retried_once_then_unreadable_and_missing_credentials_make_no_request()
    {
        var calls = 0;
        var (client, _, clock) = Make(_ => { calls++; return Json("{}", HttpStatusCode.InternalServerError); });
        var task = client.GetFundsAsync(CancellationToken.None);
        task.IsCompleted.ShouldBeFalse("the retry waits 250 ms on the (fake) clock");
        while (!task.IsCompleted)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Yield();
        }

        (await task).Reason.ShouldBe("read_failed");
        calls.ShouldBe(2);

        var (none, handler, _) = Make(_ => Json("{}"), creds: false);
        (await none.GetFundsAsync(CancellationToken.None)).Reason.ShouldBe("execution.token_unavailable");
        handler.Seen.ShouldBeEmpty();
    }
}
