using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Remark;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Options.Remark;

/// <summary>P14-08 criterion 5 (O.2 hand example, paise exact): cash entry 1,000, stop 990, quantity 100 gives a risk of 1,000; premium 40 and delta 0.5 give 200 units; the exit at 50 gives 2,000 gross and an R_equiv of 2.0 before costs.</summary>
public sealed class RemarkMoneyTests
{
    private static readonly DateOnly Day = new(2025, 6, 10);
    private static readonly HalfSpreadValue OnePercent = new(HalfSpreadSource.Assumed, 1m, "spread: assumed 1 % by owner", null, false);

    [Fact]
    public void The_hand_example_gives_200_units_2000_rupees_gross_and_an_r_equiv_of_2()
    {
        var units = RemarkMoney.Units(1000m, 0.5, 1000m, 990m);
        var gross = RemarkMoney.Gross(4000, 5000, units!.Value);

        units.ShouldBe(200m);
        gross.ShouldBe(2000.00m);
        ((double)(gross / 1000m)).ShouldBe(2.0);
    }

    [Fact]
    public void With_a_test_option_sheet_and_a_one_percent_half_spread_the_net_value_is_the_hand_value_to_the_paisa()
    {
        // buy 8,000.00: 20.00 brokerage, 2.40 exchange, 0.01 SEBI, 0.24 stamp, GST 18 % of 22.41 = 4.03 -> 26.68; sell 10,000.00: 20.00, STT 10.00, 3.00, 0.01, GST 4.14 -> 37.15; spread 1 % of each leg: 80.00 + 100.00
        var net = RemarkMoney.Net(RemarkKit.OptionSheet, 2000.00m, 200m, 4000, 5000, Day, Day, OnePercent, OnePercent);

        net.Fate.ShouldBeNull();
        net.NetInr.ShouldBe(1756.17m);
        net.Labels.ShouldContain("spread: assumed 1 % by owner");
        ((double)(net.NetInr!.Value / 1000m)).ShouldBe(1.75617);
    }

    [Fact]
    public void A_sheet_without_option_lines_is_basis_mismatch_and_an_unavailable_spread_is_spread_unavailable_never_zero()
    {
        RemarkMoney.Net(TestCostSheet.Sheet, 2000m, 200m, 4000, 5000, Day, Day, OnePercent, OnePercent).Fate.ShouldBe(RemarkFates.BasisMismatch);

        var unavailable = RemarkMoney.Net(RemarkKit.OptionSheet, 2000m, 200m, 4000, 5000, Day, Day, OnePercent, HalfSpreadValue.Unavailable);

        unavailable.Fate.ShouldBe(RemarkFates.SpreadUnavailable);
        unavailable.NetInr.ShouldBeNull();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void A_delta_that_is_not_a_usable_number_gives_no_units(double delta) => RemarkMoney.Units(1000m, delta, 1000m, 990m).ShouldBeNull();

    [Fact]
    public void A_stop_at_the_entry_or_no_risk_gives_no_units() => RemarkMoney.Units(1000m, 0.5, 1000m, 1000m).ShouldBeNull();
}
