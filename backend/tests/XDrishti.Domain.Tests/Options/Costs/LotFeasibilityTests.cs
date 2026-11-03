using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Costs;

namespace XDrishti.Domain.Tests.Options.Costs;

/// <summary>P14-07 criterion 6: one lot against a cash trade's rupee risk, with the lot in force on the date from the stored rows.</summary>
public sealed class LotFeasibilityTests
{
    private static readonly Guid Nifty = Guid.Parse("00000000-0000-7000-8000-000000000013");
    private static readonly Guid Stock = Guid.Parse("00000000-0000-7000-8000-000000000099");

    private static readonly LotSizeEntry[] Rows =
    [
        new(1, Nifty, new DateOnly(2024, 11, 20), 75, LotSizeEntry.Secondary, false),
        new(2, Nifty, new DateOnly(2025, 12, 30), 65, LotSizeEntry.Secondary, false),
        new(3, Stock, new DateOnly(2026, 11, 1), 500, LotSizeEntry.ScripMaster, false),
    ];

    // the cash trade: stop 80 points away, 12.5 units (a fractional quantity) -> a rupee risk of 1,000
    private const decimal Entry = 22_000m;
    private const decimal Stop = 21_920m;
    private const decimal Quantity = 12.5m;

    [Fact]
    public void The_nifty_example_loses_2600_a_lot_against_a_risk_of_1000_and_is_not_sizable_with_the_unconfirmed_label()
    {
        var lot = LotSizeLookup.InForce(Rows, Nifty, new DateOnly(2026, 1, 5));

        var result = LotFeasibility.Evaluate(lot, 0.5, Entry, Stop, Quantity);

        result.Verdict.ShouldBe(FeasibilityVerdict.NotSizable);
        result.Code.ShouldBe("not_sizable");
        result.LotSize.ShouldBe(65);
        result.LossPerLotRupees.ShouldBe(2_600m);
        result.RiskRupees.ShouldBe(1_000m);
        result.Label.ShouldBe("lot: unconfirmed");
    }

    [Fact]
    public void The_same_trade_uses_lot_75_on_2025_12_29_and_lot_65_on_2025_12_30()
    {
        var before = LotFeasibility.Evaluate(LotSizeLookup.InForce(Rows, Nifty, new DateOnly(2025, 12, 29)), 0.5, Entry, Stop, Quantity);
        var after = LotFeasibility.Evaluate(LotSizeLookup.InForce(Rows, Nifty, new DateOnly(2025, 12, 30)), 0.5, Entry, Stop, Quantity);

        before.LotSize.ShouldBe(75);
        before.LossPerLotRupees.ShouldBe(3_000m);
        after.LotSize.ShouldBe(65);
        after.LossPerLotRupees.ShouldBe(2_600m);
    }

    [Fact]
    public void A_lot_that_fits_the_risk_is_sizable_and_a_loss_equal_to_the_risk_is_sizable()
    {
        var lot = LotSizeLookup.InForce(Rows, Nifty, new DateOnly(2026, 1, 5));

        LotFeasibility.Evaluate(lot, 0.5, Entry, Stop, 40m).Verdict.ShouldBe(FeasibilityVerdict.Sizable); // risk 3,200 > 2,600
        LotFeasibility.Evaluate(lot, 0.5, Entry, Stop, 32.5m).Verdict.ShouldBe(FeasibilityVerdict.Sizable); // risk 2,600 = loss
        LotFeasibility.Evaluate(lot, 0.5, Entry, Stop, 32.4m).Verdict.ShouldBe(FeasibilityVerdict.NotSizable);
        LotFeasibility.Evaluate(new LotInForce(true, 65, true, new DateOnly(2025, 12, 30), "circular"), 0.5, Entry, Stop, 40m).Label.ShouldBeNull();
    }

    [Fact]
    public void A_stock_date_before_its_first_stored_lot_is_lot_unknown_and_the_risk_in_fractional_units_is_still_computed()
    {
        var lot = LotSizeLookup.InForce(Rows, Stock, new DateOnly(2026, 10, 31));

        var result = LotFeasibility.Evaluate(lot, 0.5, Entry, Stop, Quantity);

        result.Verdict.ShouldBe(FeasibilityVerdict.LotUnknown);
        result.Code.ShouldBe("lot_unknown");
        result.RiskRupees.ShouldBe(1_000m);
        result.LossPerLotRupees.ShouldBeNull();
        result.LotSize.ShouldBeNull();
    }

    [Fact]
    public void An_unavailable_delta_or_a_stop_at_the_entry_is_never_sizable()
    {
        var lot = LotSizeLookup.InForce(Rows, Nifty, new DateOnly(2026, 1, 5));

        LotFeasibility.Evaluate(lot, null, Entry, Stop, Quantity).Verdict.ShouldBe(FeasibilityVerdict.DeltaUnavailable);
        LotFeasibility.Evaluate(lot, double.NaN, Entry, Stop, Quantity).Verdict.ShouldBe(FeasibilityVerdict.DeltaUnavailable);
        LotFeasibility.Evaluate(lot, 1.5, Entry, Stop, Quantity).Verdict.ShouldBe(FeasibilityVerdict.DeltaUnavailable); // a delta beyond 1 is not a delta
        LotFeasibility.Evaluate(lot, -1e30, Entry, Stop, Quantity).Verdict.ShouldBe(FeasibilityVerdict.DeltaUnavailable); // and must not overflow
        LotFeasibility.Evaluate(lot, double.PositiveInfinity, Entry, Stop, Quantity).Verdict.ShouldBe(FeasibilityVerdict.DeltaUnavailable);
        LotFeasibility.Evaluate(lot, -1.0, Entry, Stop, Quantity).Verdict.ShouldBe(FeasibilityVerdict.NotSizable);
        LotFeasibility.Evaluate(lot, 0.5, Entry, Entry, Quantity).Verdict.ShouldBe(FeasibilityVerdict.RiskInvalid);
        LotFeasibility.Evaluate(lot, 0.5, Entry, Stop, 0m).Verdict.ShouldBe(FeasibilityVerdict.RiskInvalid);
        default(FeasibilityVerdict).ShouldBe(FeasibilityVerdict.Unknown);
    }
}
