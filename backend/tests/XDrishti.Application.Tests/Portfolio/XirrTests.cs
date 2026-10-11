using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>
/// XIRR against an independent reference. The expected rates of <see cref="Spreadsheet"/> were computed for this test with a separate implementation
/// of the definition every spreadsheet uses (Excel / LibreOffice <c>XIRR</c>: Σ amount / (1 + r)^((date − first date) / 365) = 0): plain bisection with
/// 60-digit decimal arithmetic in Python 3 (<c>python3 -I</c>; numpy and scipy are not installed in the sandbox), sharing no code or algorithm with
/// the Newton solver under test:
/// <code>
/// npv(r) = sum(Decimal(a) / (1 + r) ** (Decimal((d - d0).days) / 365) for d, a in flows)   # d0 = first date
/// bisect npv on [-0.9999, 1e6] for 400 halvings; print 12 digits
/// </code>
/// </summary>
public sealed class XirrTests
{
    private const double Tolerance = 1e-6;

    private static CashFlow F(int y, int m, int d, decimal amount) => new(new DateOnly(y, m, d), amount);

    public static TheoryData<string, CashFlow[], double> Spreadsheet => new()
    {
        // The worked example of docs/design/12 section 4: buy 1,00,000 on 1 Jan, add 50,000 on 1 Apr, value 1,72,000 on 31 Dec (about 16.1 %).
        { "doc example", [F(2025, 1, 1, -100000), F(2025, 4, 1, -50000), F(2025, 12, 31, 172000)], 0.161134550207 },
        { "single lot, 731 days, +21 %", [F(2024, 1, 1, -1000), F(2026, 1, 1, 1210)], 0.099856587738 },
        { "three lots, rising", [F(2023, 3, 15, -100), F(2024, 6, 30, -200), F(2025, 2, 10, -150), F(2026, 10, 11, 620)], 0.142954111871 },
        { "loss on one lot", [F(2024, 5, 20, -5000), F(2026, 10, 11, 4200)], -0.070225769749 },
        { "closed lot plus open value", [F(2024, 1, 10, -1000), F(2024, 8, 5, -500), F(2025, 3, 3, 700), F(2026, 10, 11, 900)], 0.035501258956 },
        { "high return in under a year", [F(2025, 12, 1, -1000), F(2026, 10, 11, 2500)], 1.901172665640 },
        { "sixteen years", [F(2010, 4, 1, -10000), F(2026, 10, 11, 120000)], 0.162111585504 },
        { "exactly 30 days", [F(2026, 9, 11, -1000), F(2026, 10, 11, 1020)], 0.272434462840 },
        { "near total loss", [F(2024, 1, 1, -100000), F(2026, 1, 1, 1500)], -0.877173189880 },
        { "lots on the same day", [F(2025, 6, 2, -300), F(2025, 6, 2, -700), F(2026, 6, 2, 1100)], 0.100000000000 },
    };

    [Theory]
    [MemberData(nameof(Spreadsheet))]
    public void The_rate_matches_the_spreadsheet_within_a_millionth(string name, CashFlow[] flows, double expected)
    {
        var result = Xirr.Compute(flows);

        result.State.ShouldBe(XirrState.Ok, name);
        result.Rate.ShouldNotBeNull().ShouldBe(expected, Tolerance, name);
    }

    [Fact]
    public void The_order_of_the_flows_does_not_matter()
    {
        CashFlow[] flows = [F(2026, 10, 11, 620), F(2025, 2, 10, -150), F(2023, 3, 15, -100), F(2024, 6, 30, -200)];

        Xirr.Compute(flows).Rate.ShouldNotBeNull().ShouldBe(0.142954111871, Tolerance);
    }

    [Fact]
    public void The_rate_found_makes_the_net_present_value_zero()
    {
        CashFlow[] flows = [F(2023, 3, 15, -100), F(2024, 6, 30, -200), F(2025, 2, 10, -150), F(2026, 10, 11, 620)];
        var rate = Xirr.Compute(flows).Rate!.Value;

        var npv = flows.Sum(f => (double)f.Amount / Math.Pow(1 + rate, (f.Date.DayNumber - flows[0].Date.DayNumber) / 365.0));

        npv.ShouldBe(0.0, 1e-6);
    }

    [Fact]
    public void No_flows_means_no_data()
    {
        Xirr.Compute([]).State.ShouldBe(XirrState.NoData);
        Xirr.Compute([F(2025, 1, 1, 0)]).State.ShouldBe(XirrState.NoData);
    }

    [Fact]
    public void Only_outflows_or_only_inflows_are_insufficient()
    {
        Xirr.Compute([F(2024, 1, 1, -100), F(2026, 1, 1, -50)]).State.ShouldBe(XirrState.Insufficient);
        Xirr.Compute([F(2024, 1, 1, 100), F(2026, 1, 1, 50)]).State.ShouldBe(XirrState.Insufficient);
    }

    [Fact]
    public void A_total_loss_with_nothing_coming_back_is_insufficient()
    {
        // The value today is 0: there is no inflow at all (a price of zero).
        Xirr.Compute([F(2024, 1, 1, -1000)]).State.ShouldBe(XirrState.Insufficient);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    public void A_period_under_thirty_days_is_insufficient(int days, bool meaningful)
    {
        var first = new DateOnly(2026, 9, 1);

        var result = Xirr.Compute([new CashFlow(first, -1000), new CashFlow(first.AddDays(days), 1010)]);

        result.State.ShouldBe(meaningful ? XirrState.Ok : XirrState.Insufficient);
        result.Rate.HasValue.ShouldBe(meaningful);
    }

    [Fact]
    public void Same_day_flows_are_insufficient()
    {
        Xirr.Compute([F(2026, 10, 11, -1000), F(2026, 10, 11, 1100)]).State.ShouldBe(XirrState.Insufficient);
    }

    [Fact]
    public void A_rate_beyond_1000_percent_is_not_meaningful_but_still_reported_when_it_is_found()
    {
        // x2 in 60 days: 6,680 % a year (reference value 66.805638038995).
        var result = Xirr.Compute([F(2026, 8, 12, -1000), F(2026, 10, 11, 2000)]);

        result.State.ShouldBe(XirrState.NotMeaningful);
        result.Rate.ShouldNotBeNull().ShouldBe(66.805638038995, 1e-4);
    }

    [Fact]
    public void A_rate_beyond_the_solver_range_is_not_meaningful_too()
    {
        // x3 in 60 days: 79,789 % a year, above the 10,000 % the solver searches.
        var result = Xirr.Compute([F(2026, 8, 12, -1000), F(2026, 10, 11, 3000)]);

        result.ShouldBe(new XirrResult(XirrState.NotMeaningful, null));
    }

    [Fact]
    public void A_loss_beyond_what_the_solver_range_can_express_does_not_converge_and_is_insufficient()
    {
        // 1,00,000 became one paisa: the rate is below -99.99 % (about -99.999 %), outside [-0.9999, 100].
        Xirr.Compute([F(2024, 1, 1, -100000), F(2025, 6, 1, 0.01m)]).State.ShouldBe(XirrState.Insufficient);
    }

    [Fact]
    public void Several_sign_changes_still_give_a_rate_that_solves_the_equation()
    {
        CashFlow[] flows = [F(2023, 1, 2, -1000), F(2024, 1, 2, 1500), F(2024, 7, 1, -600), F(2026, 10, 11, 400)];

        var result = Xirr.Compute(flows);

        result.State.ShouldBe(XirrState.Ok);
        var rate = result.Rate!.Value;
        var npv = flows.Sum(f => (double)f.Amount / Math.Pow(1 + rate, (f.Date.DayNumber - flows[0].Date.DayNumber) / 365.0));
        Math.Abs(npv).ShouldBeLessThan(1e-4);
    }
}
