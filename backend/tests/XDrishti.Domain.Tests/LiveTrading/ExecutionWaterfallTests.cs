using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-02 criterion 6: the waterfall of a round trip adds up exactly; the fixture is T1 of P11-01 (plan exit 1,264, live exit 1,262 on 40 shares, live charges 38.40).</summary>
public sealed class ExecutionWaterfallTests
{
    private static WaterfallResult T1(decimal? charges = 38.40m, double planCharges = 0) =>
        ExecutionWaterfall.Build(Side.Buy, 1250m, 1264m, 1240m, 1250.475m, 1262m, 40, charges, planCharges);

    [Fact]
    public void The_fixture_gives_the_spec_rows_and_the_rows_add_up()
    {
        var w = T1();
        w.Basis.ShouldBe(ExecutionWaterfall.Net);
        w.Balanced.ShouldBeTrue();
        ExecutionWaterfall.Adds(w.Rows).ShouldBeTrue();
        double[] expected = [1.4, 0.0475, 0.2, 1.1525, 0.096, 1.0565];
        for (var i = 0; i < expected.Length; i++)
        {
            w.Rows[i].R.ShouldNotBeNull().ShouldBe(expected[i], 1e-9);
        }

        w.Rows[3].Rupees.ShouldBe(461.0m); // 1.1525 R x 10 x 40
        w.Rows[4].Rupees.ShouldBe(38.40m);
    }

    [Fact]
    public void Without_live_charges_the_result_is_gross_and_no_net_is_shown()
    {
        var w = T1(charges: null);
        w.Basis.ShouldBe(ExecutionWaterfall.Gross);
        w.Rows.Select(r => r.Label).ShouldNotContain(l => l.Contains("net", StringComparison.Ordinal));
        w.Rows.Count.ShouldBe(4);
        w.Notes.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_plan_without_a_stop_shows_no_R_but_the_rupees()
    {
        var w = ExecutionWaterfall.Build(Side.Buy, 1250m, 1264m, null, 1250.475m, 1262m, 40, null, 0);
        w.Rows.ShouldAllBe(r => r.R == null);
        w.Rows[3].Rupees.ShouldBe(461.0m);
        w.Balanced.ShouldBeTrue();
    }

    [Fact]
    public void The_plans_own_charges_are_subtracted_from_the_live_charges()
    {
        var w = T1(planCharges: 0.04); // 0.04 R x 10 x 40 = 16 rupees of plan charges
        w.Rows[4].Rupees.ShouldBe(22.40m);
        w.Rows[4].R.ShouldNotBeNull().ShouldBe(0.056, 1e-9);
    }

    [Fact]
    public void A_short_trip_mirrors_the_signs()
    {
        var w = ExecutionWaterfall.Build(Side.Sell, 800m, 790m, 805m, 799.70m, 791m, 100, null, 0);
        w.Rows[0].Rupees.ShouldBe(1000m);
        w.Rows[1].Rupees.ShouldBe(30m);
        w.Rows[2].Rupees.ShouldBe(100m);
        w.Rows[3].Rupees.ShouldBe(870m);
        ExecutionWaterfall.Adds(w.Rows).ShouldBeTrue();
    }

    [Fact]
    public void The_identity_holds_for_random_prices_and_a_doctored_exit_price_breaks_it()
    {
        var random = new Random(11);
        for (var i = 0; i < 300; i++)
        {
            var side = random.Next(2) == 0 ? Side.Buy : Side.Sell;
            decimal P() => Math.Round(50m + ((decimal)random.NextDouble() * 500m), 4);
            var w = ExecutionWaterfall.Build(side, P(), P(), P(), P(), P(), random.Next(1, 500), random.Next(2) == 0 ? null : 12.5m, 0);
            w.Balanced.ShouldBeTrue();
            ExecutionWaterfall.Adds(w.Rows).ShouldBeTrue();
        }

        var good = T1();
        var doctored = good.Rows.Select(r => r.Label == "Exit difference" ? r with { Rupees = r.Rupees + 0.01m } : r).ToList();
        ExecutionWaterfall.Adds(doctored).ShouldBeFalse();
    }
}
