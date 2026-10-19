using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>Criteria 8 to 10 of P3-03c: the quantity (floor, never rounding; the exact entry; no division by zero), its properties on 500 seeded days, the sizing profile and the cost context.</summary>
public sealed class SizingTests
{
    private static readonly SizingProfile Standard = SizingProfile.Standard;

    private static readonly string[] NameCases = ["", " ", "a|b\nc", "A", "a b", "-x", "a\nb"];

    private static long Quantity(decimal capital, decimal entry, decimal risk, SizingProfile? profile = null) => Sizing.Quantity(capital, profile ?? Standard, entry, risk).Value;

    [Fact]
    public void The_worked_trades_W1_W2_W4_and_W7_are_sized_2500_1251_0_and_2000()
    {
        Quantity(1_000_000m, 100.00m, 1.00m).ShouldBe(2_500, "W1: byRisk 5,000, byCap 2,500: the cap binds");
        Quantity(1_000_000m, 199.80m, 2.20m).ShouldBe(1_251, "W2: byRisk 2,272.7, byCap 1,251.25");
        Quantity(10_000m, 3_000.00m, 30.00m).ShouldBe(0, "W4: byCap 0.8333 is cut to 0, not rounded up");
        Quantity(1_000_000m, 100.50m, 2.50m).ShouldBe(2_000, "W7: byRisk 2,000, byCap 2,487.56: the risk binds");
    }

    [Fact]
    public void A_by_cap_of_exactly_one_gives_one_and_just_under_gives_zero()
    {
        Quantity(10_000m, 2_500.00m, 1.00m).ShouldBe(1, "byCap = 2,500 / 2,500 = 1.0000");
        Quantity(10_000m, 2_500.000025m, 1.00m).ShouldBe(0, "byCap = 0.99999999");
    }

    [Fact]
    public void The_quantity_is_cut_and_never_rounded()
    {
        var onePercent = SizingProfile.Create("test", 1m, 100m).Value;

        Quantity(29_999m, 1m, 100m, onePercent).ShouldBe(2, "byRisk 2.9999 gives 2");
        Quantity(29_999.5m, 1m, 100m, onePercent).ShouldBe(2);
        Quantity(30_000m, 1m, 100m, onePercent).ShouldBe(3);
    }

    [Theory]
    [InlineData(0, 100, 1, "capital")]
    [InlineData(-1, 100, 1, "capital")]
    [InlineData(1000, 0, 1, "entry")]
    [InlineData(1000, -5, 1, "entry")]
    [InlineData(1000, 100, 0, "risk")]
    [InlineData(1000, 100, -1, "risk")]
    public void A_capital_entry_or_risk_that_is_not_above_zero_is_an_error_and_never_an_exception_from_a_division(int capital, int entry, int risk, string field)
    {
        var sized = Sizing.Quantity(capital, Standard, entry, risk);

        sized.IsFailure.ShouldBeTrue();
        sized.Error.Code.ShouldBe(CostErrors.SizingInputInvalidCode);
        sized.Error.Message.ShouldContain(field);
    }

    [Fact]
    public void A_null_profile_is_a_caller_error()
    {
        Should.Throw<ArgumentNullException>(() => Sizing.Quantity(1000m, null!, 100m, 1m));
    }

    private static IEnumerable<(decimal Capital, decimal Entry, decimal Risk)> SeededDays()
    {
        var random = new Random(20261019);
        for (var i = 0; i < 500; i++)
        {
            var capital = Math.Round(10_000m + ((decimal)random.NextDouble() * 100_000_000m), 2);
            var entry = Math.Round(20m + ((decimal)random.NextDouble() * 4_980m), 2);
            var stopFraction = 0.002m + ((decimal)random.NextDouble() * 0.028m);
            var risk = Math.Max(0.05m, Math.Round(entry * stopFraction, 2));
            yield return (capital, entry, risk);
        }
    }

    [Fact]
    public void The_risk_taken_and_the_cost_of_the_position_stay_inside_the_budget_on_500_seeded_days()
    {
        foreach (var (capital, entry, risk) in SeededDays())
        {
            var q = Quantity(capital, entry, risk);

            (q * risk).ShouldBeLessThanOrEqualTo(capital * Standard.RiskPerTradePct / 100m);
            (q * entry).ShouldBeLessThanOrEqualTo(capital * Standard.MaxCapitalPerTradePct / 100m);
            var byRisk = capital * Standard.RiskPerTradePct / 100m / risk;
            var byCap = capital * Standard.MaxCapitalPerTradePct / 100m / entry;
            (q == 0).ShouldBe(Math.Min(byRisk, byCap) < 1, "the quantity is 0 exactly when the smaller of the two is under 1");
        }
    }

    [Fact]
    public void The_quantity_never_falls_as_capital_or_risk_per_trade_grows_and_doubling_the_capital_gives_2q_or_2q_plus_1()
    {
        var wider = SizingProfile.Create("wider", 1m, 25m).Value;
        foreach (var (capital, entry, risk) in SeededDays())
        {
            var q = Quantity(capital, entry, risk);

            Quantity(capital * 1.5m, entry, risk).ShouldBeGreaterThanOrEqualTo(q);
            Quantity(capital, entry, risk, wider).ShouldBeGreaterThanOrEqualTo(q);
            var doubled = Quantity(2 * capital, entry, risk);
            (doubled == 2 * q || doubled == (2 * q) + 1).ShouldBeTrue($"q {q}, doubled {doubled}");
        }
    }

    [Fact]
    public void A_tighter_stop_never_gives_a_smaller_by_risk_quantity_when_the_risk_binds()
    {
        var wide = SizingProfile.Create("wide", 2m, 100m).Value;
        foreach (var (capital, entry, risk) in SeededDays())
        {
            Quantity(capital, entry, risk / 2, wide).ShouldBeGreaterThanOrEqualTo(Quantity(capital, entry, risk, wide));
        }
    }

    [Fact]
    public void The_profile_refuses_zero_negative_and_too_large_numbers_and_the_two_named_ones_are_the_seeded_rows()
    {
        foreach (var (risk, cap) in new[] { (0m, 25m), (-0.5m, 25m), (2.01m, 25m), (0.5m, 0m), (0.5m, -1m), (0.5m, 100.01m) })
        {
            var created = SizingProfile.Create("x", risk, cap);

            created.IsFailure.ShouldBeTrue($"{risk}/{cap}");
            created.Error.Code.ShouldBe(CostErrors.SizingInputInvalidCode);
        }

        foreach (var name in NameCases)
        {
            SizingProfile.Create(name, 0.5m, 25m).IsFailure.ShouldBeTrue("a name of the key text must be of the pattern of a sheet id: " + name);
        }

        SizingProfile.Create(new string('x', 41), 0.5m, 25m).IsFailure.ShouldBeTrue();
        SizingProfile.Create("a-b_c.1", 0.5m, 25m).IsSuccess.ShouldBeTrue();
        SizingProfile.Create("edge", 2m, 100m).IsSuccess.ShouldBeTrue("the bounds are inclusive");
        SizingProfile.Standard.ShouldBe(SizingProfile.Create("standard", 0.5m, 25m).Value);
        (SizingProfile.Standard.Name, SizingProfile.Standard.RiskPerTradePct, SizingProfile.Standard.MaxCapitalPerTradePct).ShouldBe(("standard", 0.5m, 25m));
        (SizingProfile.Conservative.Name, SizingProfile.Conservative.RiskPerTradePct, SizingProfile.Conservative.MaxCapitalPerTradePct).ShouldBe(("conservative", 0.25m, 25m));
    }

    [Fact]
    public void The_context_refuses_a_null_sheet_or_profile_a_capital_outside_the_range_and_a_max_cost_outside_the_range()
    {
        Should.Throw<ArgumentNullException>(() => CostContext.Create(null!, Standard, 1m, 0.1m));
        Should.Throw<ArgumentNullException>(() => CostContext.Create(TestCostSheet.Sheet, null!, 1m, 0.1m));
        foreach (var capital in new[] { 0m, -1m, 10_000_000_000.01m })
        {
            CostContext.Create(TestCostSheet.Sheet, Standard, capital, 0.1m).Error.Code.ShouldBe(CostErrors.SizingInputInvalidCode);
        }

        foreach (var maxCostR in new[] { 0m, -0.1m, 100.01m })
        {
            CostContext.Create(TestCostSheet.Sheet, Standard, 1_000_000m, maxCostR).Error.Code.ShouldBe(CostErrors.SizingInputInvalidCode);
        }

        var edge = CostContext.Create(TestCostSheet.Sheet, Standard, 10_000_000_000m, 100m).Value;
        (edge.Capital, edge.MaxCostR, edge.Sheet.Id, edge.Profile.Name).ShouldBe((10_000_000_000m, 100m, "test-synthetic-1", "standard"));
    }

    [Fact]
    public void The_context_of_the_worked_examples_is_the_standard_profile_ten_lakh_and_a_tenth_of_r()
    {
        var context = TestCostSheet.Context();

        (context.Capital, context.MaxCostR, context.Profile).ShouldBe((1_000_000m, 0.10m, SizingProfile.Standard));
    }
}
