using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-01 criterion 7 (the window, the side, the quantity, the status, the product) and the unavailable rule of criterion 8.</summary>
public sealed class ProtectionCheckTests
{
    private static readonly TimeSpan Within = TimeSpan.FromSeconds(300);
    private static readonly DateTimeOffset Last = LiveFixtureDay.At("09:41:30");

    private static string Check(LiveOrder stop, string product = "INTRADAY") =>
        ProtectionCheck.Evaluate(Last, Side.Buy, product, 40, [stop], Within);

    private static LiveOrder Stop(string created, long qty = 40, string status = "pending", Side side = Side.Sell) =>
        LiveFixtureDay.Order("s", "RELIANCE", side, qty, status, 0, created, trigger: 1240m);

    [Fact]
    public void A_stop_152_seconds_after_the_last_fill_protects() => Check(Stop("09:44:02")).ShouldBe(OutcomeCodes.Protected);

    [Fact]
    public void A_stop_exactly_at_the_window_protects_and_one_second_later_does_not()
    {
        Check(Stop("09:46:30")).ShouldBe(OutcomeCodes.Protected);
        Check(Stop("09:46:31")).ShouldBe(OutcomeCodes.Unprotected);
    }

    [Fact]
    public void A_stop_of_the_wrong_side_too_small_or_cancelled_does_not_protect()
    {
        Check(Stop("09:44:02", side: Side.Buy)).ShouldBe(OutcomeCodes.Unprotected);
        Check(Stop("09:44:02", qty: 39)).ShouldBe(OutcomeCodes.Unprotected);
        Check(Stop("09:44:02", status: "cancelled")).ShouldBe(OutcomeCodes.Unprotected);
    }

    [Fact]
    public void An_order_without_a_trigger_price_is_not_a_stop()
    {
        var limit = LiveFixtureDay.Order("s", "RELIANCE", Side.Sell, 40, "pending", 0, "09:44:02", price: 1262m);

        Check(limit).ShouldBe(OutcomeCodes.Unprotected);
    }

    [Fact]
    public void A_delivery_position_with_no_stop_is_unknown_not_unprotected() =>
        Check(Stop("09:44:02", status: "cancelled"), "CNC").ShouldBe(OutcomeCodes.ProtectionUnknown);

    [Fact]
    public void No_order_evidence_is_unavailable() =>
        ProtectionCheck.Evaluate(Last, Side.Buy, "INTRADAY", 40, null, Within).ShouldBe(OutcomeCodes.Unavailable);
}
