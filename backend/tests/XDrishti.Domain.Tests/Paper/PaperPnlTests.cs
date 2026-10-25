using XDrishti.Domain.Paper;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>Spec P10-01 criteria 4 and 5 on the hand-worked fixture: the owner's quantity, the stored sheet's charges by the pure charge function, and the replay's R.</summary>
public sealed class PaperPnlTests
{
    [Fact]
    public void Gross_rupees_are_the_filled_move_times_the_owners_quantity()
    {
        PaperPnl.Gross(Side.Buy, 60, 100.15m, 102.30m).ShouldBe(129.00m);
        PaperPnl.Gross(Side.Sell, 60, 102.30m, 100.15m).ShouldBe(129.00m);
        PaperPnl.Gross(Side.Buy, 60, 102.30m, 100.15m).ShouldBe(-129.00m);
    }

    [Fact]
    public void A_flat_sheet_charges_ten_rupees_an_order_so_twenty_and_net_is_109()
    {
        var charges = PaperPnl.Charges(PaperFixture.FlatSheet, CostSegment.IntradayEquity, Side.Buy, 60, 100.15m, 102.30m, PaperFixture.Session, PaperFixture.Session);

        charges.Value.ShouldBe(20.00m);
        var exit = PaperPnl.Exit(PaperFixture.Trade(Guid.Empty, netR: 1.80), 60, PaperFixture.FlatSheet).Value;
        exit.GrossPnl.ShouldBe(129.00m);
        exit.Charges.ShouldBe(20.00m);
        exit.NetPnl.ShouldBe(109.00m);
        exit.ChargeSheetId.ShouldBe(PaperFixture.SheetId);
        exit.RNet.ShouldBe(1.80);
        exit.RSim.ShouldBe(1.80);
        exit.RBasis.ShouldBe("net");
        exit.CostBasis.ShouldBe("net of the charge sheet flat-10");
    }

    [Fact]
    public void Without_a_sheet_charges_and_net_are_null_and_r_is_the_filled_r()
    {
        var exit = PaperPnl.Exit(PaperFixture.Trade(Guid.Empty), 60, null).Value;

        exit.GrossPnl.ShouldBe(129.00m);
        exit.Charges.ShouldBeNull();
        exit.NetPnl.ShouldBeNull();
        exit.RNet.ShouldBeNull();
        exit.RExact.ShouldBe(2.0);
        exit.RSim.ShouldBe(PaperFixture.RFilled, 1e-9);
        exit.RSim.ShouldBe(1.954545, 1e-6);
        exit.RBasis.ShouldBe("gross");
        exit.CostBasis.ShouldBe(PaperWording.Gross);
        exit.ExitPrice.ShouldBe(102.30m);
        exit.ExitAt.ShouldBe(new DateTimeOffset(2026, 11, 3, 11, 5, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public void The_paper_exit_price_is_the_fill_and_may_differ_from_the_exact_price_the_grade_stores_while_r_is_the_same()
    {
        var trade = PaperFixture.Trade(Guid.Empty) with { ExitReason = ExitReason.Stop, ExitPrice = 99.00, ExitFilledPrice = 98.95, RFilled = -1.045 };

        var exit = PaperPnl.Exit(trade, 60, null).Value;

        exit.ExitPrice.ShouldBe(98.95m);
        exit.ExitPrice.ShouldNotBe((decimal)trade.ExitPrice);
        exit.RSim.ShouldBe(trade.RFilled);
    }

    [Fact]
    public void Charges_fail_when_the_sheet_has_no_rates_for_the_date()
    {
        var charges = PaperPnl.Charges(PaperFixture.FlatSheet, CostSegment.IntradayEquity, Side.Buy, 60, 100m, 101m, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1));

        charges.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_fixture_entry_instant_is_0920_india_time_and_an_entry_before_arming_is_refused()
    {
        var entry = DateTimeOffset.FromUnixTimeSeconds(PaperFixture.EntryEpoch);

        entry.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).ShouldBe("2026-11-03 09:20:00");
        PaperEntry.EntersAfterArming(entry.AddMinutes(-10), entry).ShouldBeTrue();
        PaperEntry.EntersAfterArming(entry.AddMinutes(1), entry).ShouldBeFalse();
    }

    [Fact]
    public void The_label_and_the_skip_codes_are_pinned()
    {
        PaperWording.Basis.ShouldBe("simulated on the official 1-minute bars; no order was placed or exists");
        SkipCodes.All.ShouldBe(["not_triggered", "not_gradable", "dropped_by_sizing", "legs_not_supported", "entered_before_armed", "modification_unreadable", "approval_missing"]);
    }
}
