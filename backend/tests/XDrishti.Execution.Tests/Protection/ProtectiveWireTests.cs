using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>
/// The protective wire of P12-04 against a fake handler only (nothing here reaches any network). The stop-loss order type, the trigger field, the super-order body and the modify body are from Dhan's pages and UNVERIFIED against the real service.
/// </summary>
public sealed class ProtectiveWireTests
{
    private const string Token = "SECRET-TOKEN-VALUE";

    private sealed class Creds : IExecCredentials
    {
        public DhanCredentials? Current() => new("1000000001", Token);
    }

    private sealed record Seen(HttpMethod Method, Uri Uri, string? Body);

    private sealed class Script(Func<Seen, HttpResponseMessage> step) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var seen = new Seen(request.Method, request.RequestUri!, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(seen);
            return step(seen);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static (DhanOrderClient Client, Script Handler) Make(Func<Seen, HttpResponseMessage> step)
    {
        var handler = new Script(step);
        var http = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, handler)) { Timeout = Timeout.InfiniteTimeSpan };
        return (new DhanOrderClient(http, new Creds(), ExecEnvironment.Sandbox, NullLogger<DhanOrderClient>.Instance, new FakeTimeProvider(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), handler);
    }

    private static string Cid(LegKind kind, int attempt = 1) => ClientOrderId.Create(new DateOnly(2026, 10, 26), 361, kind, attempt).Value.ToString();

    private static BrokerLeg Stop(decimal trigger = 1485m, decimal price = 1477.55m) => new(Cid(LegKind.Stop, 2), LegKind.Stop, ExecSide.Sell, "1594", 10, price, trigger);

    [Fact]
    public async Task A_protective_stop_is_a_sell_STOP_LOSS_limit_with_a_trigger_on_the_plain_order_path()
    {
        var (client, handler) = Make(_ => Json("""{"orderId":"1","orderStatus":"PENDING"}"""));

        var ack = await client.PlaceAsync(Stop(), CancellationToken.None);

        ack.Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        var seen = handler.Requests.Single();
        (seen.Method, seen.Uri.ToString()).ShouldBe((HttpMethod.Post, "https://sandbox.dhan.co/v2/orders"));
        using var doc = JsonDocument.Parse(seen.Body!);
        var r = doc.RootElement;
        (r.GetProperty("transactionType").GetString(), r.GetProperty("orderType").GetString(), r.GetProperty("triggerPrice").GetDecimal(), r.GetProperty("price").GetDecimal(), r.GetProperty("quantity").GetInt32())
            .ShouldBe(("SELL", "STOP_LOSS", 1485m, 1477.55m, 10));
        r.GetProperty("productType").GetString().ShouldBe("INTRADAY");
        seen.Body!.ShouldNotContain(Token, Case.Sensitive);
    }

    [Theory]
    [InlineData("buy", 1485.0, 1477.55, "stop_wrong_side")]
    [InlineData("sell", 0.0, 1477.55, "stop_trigger")]
    [InlineData("sell", 1485.02, 1477.55, "stop_trigger")]
    [InlineData("sell", 1485.0, 1490.0, "stop_trigger")]
    [InlineData("none", 1485.0, 1477.55, "stop_trigger")]
    public async Task A_stop_on_the_wrong_side_or_without_a_valid_trigger_or_with_a_limit_above_it_is_refused_before_any_request(string side, double trigger, double price, string reason)
    {
        var (client, handler) = Make(_ => throw new InvalidOperationException("must not be called"));
        var leg = Stop((decimal)trigger, (decimal)price) with { Side = side == "buy" ? ExecSide.Buy : ExecSide.Sell, Trigger = side == "none" ? null : (decimal)trigger };

        var ack = await client.PlaceAsync(leg, CancellationToken.None);

        ack.Outcome.ShouldBe(BrokerOutcome.NotSent);
        ack.Reason.ShouldBe("execution.instruction_invalid:" + reason);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_resting_target_leg_is_still_refused_and_a_trigger_on_any_other_leg_is_refused()
    {
        var (client, handler) = Make(_ => throw new InvalidOperationException("must not be called"));

        (await client.PlaceAsync(new BrokerLeg(Cid(LegKind.Target), LegKind.Target, ExecSide.Sell, "1594", 10, 1530m), CancellationToken.None)).Reason.ShouldBe("execution.instruction_invalid:leg_kind_not_supported");
        (await client.PlaceAsync(new BrokerLeg(Cid(LegKind.Exit), LegKind.Exit, ExecSide.Sell, "1594", 10, 1490m, 1500m), CancellationToken.None)).Reason.ShouldBe("execution.instruction_invalid:trigger_not_allowed");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_super_entry_is_the_hand_checked_body_on_the_super_path()
    {
        var (client, handler) = Make(_ => Json("""{"orderId":"9","orderStatus":"PENDING"}"""));
        var leg = new BrokerLeg(Cid(LegKind.Entry), LegKind.Entry, ExecSide.Buy, "1594", 10, 1500.00m, null, new SuperLegs(1485.00m, 1530.00m));

        var ack = await client.PlaceAsync(leg, CancellationToken.None);

        ack.Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        var seen = handler.Requests.Single();
        (seen.Method, seen.Uri.ToString()).ShouldBe((HttpMethod.Post, "https://sandbox.dhan.co/v2/super/orders"));
        using var doc = JsonDocument.Parse(seen.Body!);
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["dhanClientId", "correlationId", "transactionType", "exchangeSegment", "productType", "orderType", "securityId", "quantity", "price", "targetPrice", "stopLossPrice", "trailingJump"]);
        var r = doc.RootElement;
        (r.GetProperty("correlationId").GetString(), r.GetProperty("targetPrice").GetDecimal(), r.GetProperty("stopLossPrice").GetDecimal(), r.GetProperty("trailingJump").GetInt32(), r.GetProperty("price").GetDecimal()).ShouldBe(("XD2610260000A1E1", 1530m, 1485m, 0, 1500m));
    }

    [Theory]
    [InlineData(1500.0, 1500.0, 1530.0)]
    [InlineData(1500.0, 1485.0, 1500.0)]
    [InlineData(1500.0, 0.0, 1530.0)]
    [InlineData(1500.0, 1485.02, 1530.0)]
    public async Task A_super_entry_with_a_stop_not_below_the_entry_or_a_target_not_above_it_is_refused(double entry, double stop, double target)
    {
        var (client, handler) = Make(_ => throw new InvalidOperationException("must not be called"));
        var leg = new BrokerLeg(Cid(LegKind.Entry), LegKind.Entry, ExecSide.Buy, "1594", 10, (decimal)entry, null, new SuperLegs((decimal)stop, (decimal)target));

        (await client.PlaceAsync(leg, CancellationToken.None)).Reason.ShouldBe("execution.instruction_invalid:super_legs");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Changing_a_stop_looks_it_up_then_puts_STOP_LOSS_with_the_new_trigger_and_limit()
    {
        var (client, handler) = Make(seen => seen.Method == HttpMethod.Get
            ? Json($$"""{"orderId":"77","correlationId":"{{Cid(LegKind.Stop, 2)}}","transactionType":"SELL","securityId":"1594","quantity":10,"price":1477.55}""")
            : Json("""{"orderId":"77","orderStatus":"PENDING"}"""));

        var ack = await client.ModifyStopAsync(Cid(LegKind.Stop, 2), 1490m, 1482.55m, CancellationToken.None);

        ack.Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        var put = handler.Requests.Single(r => r.Method == HttpMethod.Put);
        put.Uri.ToString().ShouldBe("https://sandbox.dhan.co/v2/orders/77");
        using var doc = JsonDocument.Parse(put.Body!);
        (doc.RootElement.GetProperty("orderType").GetString(), doc.RootElement.GetProperty("triggerPrice").GetDecimal(), doc.RootElement.GetProperty("price").GetDecimal(), doc.RootElement.GetProperty("quantity").GetInt32())
            .ShouldBe(("STOP_LOSS", 1490m, 1482.55m, 10));
    }

    [Fact]
    public async Task A_stop_change_refuses_a_non_stop_a_limit_above_the_trigger_a_buy_and_the_plain_modify_of_a_stop()
    {
        var (client, handler) = Make(seen => Json($$"""{"orderId":"77","correlationId":"{{Cid(LegKind.Stop, 2)}}","transactionType":"BUY","securityId":"1594","quantity":10,"price":1477.55}"""));

        (await client.ModifyStopAsync(Cid(LegKind.Entry), 1490m, 1482.55m, CancellationToken.None)).Reason.ShouldBe("execution.instruction_invalid:stop");
        (await client.ModifyStopAsync(Cid(LegKind.Stop, 2), 1490m, 1495m, CancellationToken.None)).Reason.ShouldBe("execution.instruction_invalid:stop");
        (await client.ModifyStopAsync(Cid(LegKind.Stop, 2), 1490m, 1482.55m, CancellationToken.None)).Reason.ShouldBe("execution.instruction_invalid:stop_wrong_side");
        (await client.ModifyAsync(Cid(LegKind.Stop, 2), 1482.55m, CancellationToken.None)).Reason.ShouldBe("execution.instruction_invalid:use_modify_stop");
        handler.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public void The_guard_table_gains_exactly_one_write_the_super_entry_post_and_nothing_for_super_changes_or_other_hosts()
    {
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, HttpMethod.Post, new Uri("https://sandbox.dhan.co/v2/super/orders")).ShouldBeTrue();
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Live, HttpMethod.Post, new Uri("https://sandbox.dhan.co/v2/super/orders")).ShouldBeFalse();
        ExecutionEndpoints.IsAllowed(ExecEnvironment.None, HttpMethod.Post, new Uri("https://sandbox.dhan.co/v2/super/orders")).ShouldBeFalse();
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, HttpMethod.Put, new Uri("https://sandbox.dhan.co/v2/super/orders/1")).ShouldBeFalse();
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, HttpMethod.Delete, new Uri("https://sandbox.dhan.co/v2/super/orders/1/STOP_LOSS_LEG")).ShouldBeFalse();
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, HttpMethod.Post, new Uri("https://api.dhan.co/v2/super/orders")).ShouldBeFalse();
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, HttpMethod.Post, new Uri("https://sandbox.dhan.co/v2/super/orders?x=1")).ShouldBeFalse();
        Enum.GetValues<DhanCall>().Length.ShouldBe(10);
    }

    [Fact]
    public async Task The_order_book_read_carries_the_trigger_and_the_leg_name_when_the_broker_sends_them()
    {
        var (client, _) = Make(_ => Json("""[{"orderId":"5","correlationId":"C","orderStatus":"PENDING","securityId":"1594","transactionType":"SELL","quantity":10,"filled_qty":0,"price":1477.55,"triggerPrice":1485.0,"legName":"STOP_LOSS_LEG"},{"orderId":"6","orderStatus":"TRADED","securityId":"1594","transactionType":"BUY","quantity":10,"price":1500.0}]"""));

        var read = await client.GetOrderBookAsync(CancellationToken.None);

        read.Readable.ShouldBeTrue();
        read.Value![0].Trigger.ShouldBe(1485m);
        read.Value[0].LegName.ShouldBe("STOP_LOSS_LEG");
        read.Value[1].Trigger.ShouldBeNull();
    }
}
