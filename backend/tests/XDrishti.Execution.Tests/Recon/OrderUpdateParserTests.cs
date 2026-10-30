using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Tests.Recon;

/// <summary>P12-06 criteria 1 and 2: the tolerant reader of the order-update message and the code tables.</summary>
public sealed class OrderUpdateParserTests
{
    // Modelled on the documented sample: it is not valid JSON (a blank value after a colon, the same key in two cases, a field before and after).
    private const string Sample = """
        {
        "Data": {
        "Exchange": "NSE", "Segment": "E", "Source": "P",
        "SecurityId": "1594", "ClientId": "1000000789",
        "ExchOrderNo": "1400000000404591", "OrderNo": "1124091136546", "CorrelationId": "XD261030000169E1",
        "Product": "I", "TxnType": "B", "OrderType": "LMT",
        "Quantity": 10, "TradedQty": 4, "Price": 100.5, "TriggerPrice": 0, "AvgTradedPrice": 100.25,
        "Remarks": ,
        "LastUpdatedTime": "2026-10-30 09:21:07", "LegNo": 1,
        "RefLtp": 13.1, "refLtp": 99.9, "orderno": "SECOND",
        "Status": "PENDING",
        "AlgoId": "0"
        },
        "Type": "order_alert"
        }
        """;

    private static readonly string[] Products = ["C", "I", "M", "F", "V", "B"];
    private static readonly string[] OrderTypes = ["LMT", "MKT", "SL", "SLM"];

    [Fact]
    public void The_documented_sample_with_duplicate_keys_a_blank_value_and_mixed_case_parses_to_one_update_and_the_first_non_empty_value_wins()
    {
        var parsed = OrderUpdateParser.Parse(Sample);

        parsed.Kind.ShouldBe(UpdateParseKind.Update);
        var u = parsed.Update!;
        u.OrderNo.ShouldBe("1124091136546", "the first OrderNo wins over the later 'orderno'");
        u.CorrelationId.ShouldBe("XD261030000169E1");
        (u.Status, u.TradedQty, u.Quantity, u.LegNo, u.Side).ShouldBe((UpdateStatus.Pending, 4, 10, 1, ExecSide.Buy));
        (u.Product, u.OrderType, u.Price, u.AvgTradedPrice, u.Source).ShouldBe(("INTRADAY", "LIMIT", 100.5m, 100.25m, "P"));
        u.LegStatus.ShouldBe(LegStatus.PartTraded, "a pending order with a part fill is a part fill");
    }

    [Fact]
    public void Unknown_fields_are_ignored_and_the_remarks_and_the_client_id_are_not_kept_anywhere()
    {
        var u = OrderUpdateParser.Parse(Sample).Update!;

        u.ToString().ShouldNotContain("1000000789");
        u.ToString().ShouldNotContain("Remarks");
        typeof(OrderUpdate).GetProperties().Select(p => p.Name).Intersect(["ClientId", "Remarks", "Token"]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("not a message at all", "no_fields")]
    [InlineData("{\"Data\":{\"OrderNo\":\"1\"}}", "no_type")]
    [InlineData("{\"Data\":{\"Status\":\"PENDING\"},\"Type\":\"order_alert\"}", "order_no")]
    public void Garbage_and_incomplete_messages_are_unreadable_with_a_reason(string? text, string reason)
    {
        var parsed = OrderUpdateParser.Parse(text);

        parsed.Kind.ShouldBe(UpdateParseKind.Unreadable);
        parsed.Reason.ShouldBe(reason);
        parsed.Update.ShouldBeNull();
    }

    [Fact]
    public void A_message_that_is_not_an_order_alert_is_ignored_not_called_garbage()
    {
        OrderUpdateParser.Parse("{\"Type\":\"login_response\",\"Status\":\"ok\"}").Kind.ShouldBe(UpdateParseKind.NotAnOrderAlert);
    }

    [Fact]
    public void A_message_over_16_kilobytes_is_dropped_unread()
    {
        OrderUpdateParser.Parse(new string('x', OrderUpdateParser.MaxChars + 1)).Reason.ShouldBe("too_long");
    }

    [Theory]
    [InlineData("TRANSIT", UpdateStatus.Transit)]
    [InlineData("PENDING", UpdateStatus.Pending)]
    [InlineData("REJECTED", UpdateStatus.Rejected)]
    [InlineData("CANCELLED", UpdateStatus.Cancelled)]
    [InlineData("TRADED", UpdateStatus.Traded)]
    [InlineData("EXPIRED", UpdateStatus.Expired)]
    [InlineData("traded", UpdateStatus.Traded)]
    public void Each_status_code_maps_to_the_domain_status(string code, UpdateStatus expected) => UpdateMapping.Status(code).ShouldBe(expected);

    [Fact]
    public void Each_product_side_and_order_type_code_maps_and_an_unknown_code_maps_to_nothing()
    {
        Products.Select(UpdateMapping.Product).ShouldBe(["CNC", "INTRADAY", "MARGIN", "MTF", "CO", "BO"]);
        OrderTypes.Select(UpdateMapping.OrderType).ShouldBe(["LIMIT", "MARKET", "STOP_LOSS", "STOP_LOSS_MARKET"]);
        (UpdateMapping.Side("B"), UpdateMapping.Side("S")).ShouldBe((ExecSide.Buy, ExecSide.Sell));
        new[] { UpdateMapping.Status("PARTIAL"), UpdateMapping.Status(null), UpdateMapping.Status("") }.ShouldAllBe(s => s == null);
        (UpdateMapping.Product("Z"), UpdateMapping.OrderType("STOP"), UpdateMapping.Side("X")).ShouldBe((null, null, null));
    }

    [Theory]
    [InlineData("Status", "\"Status\": \"PENDING\"", "\"Status\": \"WEIRD\"", "status_unknown")]
    [InlineData("Product", "\"Product\": \"I\"", "\"Product\": \"Q\"", "product_unknown")]
    [InlineData("TxnType", "\"TxnType\": \"B\"", "\"TxnType\": \"X\"", "txn_type_unknown")]
    [InlineData("OrderType", "\"OrderType\": \"LMT\"", "\"OrderType\": \"ICE\"", "order_type_unknown")]
    [InlineData("LegNo", "\"LegNo\": 1", "\"LegNo\": 7", "leg_no_unknown")]
    [InlineData("TradedQty", "\"TradedQty\": 4", "\"TradedQty\": 11", "traded_qty")]
    public void An_unknown_code_or_an_impossible_figure_is_unreadable_and_never_a_guess(string name, string from, string to, string reason)
    {
        var text = Sample.Replace(from, to, StringComparison.Ordinal);

        name.ShouldNotBeNullOrEmpty();
        text.ShouldNotBe(Sample);
        OrderUpdateParser.Parse(text).Reason.ShouldBe(reason);
    }

    [Fact]
    public void The_login_message_is_exactly_the_documented_one_and_holds_the_values_it_was_given_only()
    {
        OrderUpdateParser.LoginMessage("CLIENT-1", "TOKEN-1").ShouldBe("{\"LoginReq\":{\"MsgCode\":42,\"ClientId\":\"CLIENT-1\",\"Token\":\"TOKEN-1\"},\"UserType\":\"SELF\"}");
        Should.Throw<ArgumentException>(() => OrderUpdateParser.LoginMessage(" ", "t"));
    }

    [Fact]
    public void The_update_key_changes_with_status_traded_quantity_and_time()
    {
        var a = OrderUpdateParser.Parse(Sample).Update!;

        a.Key.ShouldBe(OrderUpdateParser.Parse(Sample).Update!.Key);
        (a with { TradedQty = 5 }).Key.ShouldNotBe(a.Key);
        (a with { LastUpdatedTime = "later" }).Key.ShouldNotBe(a.Key);
        (a with { Status = UpdateStatus.Traded }).Key.ShouldNotBe(a.Key);
    }
}
