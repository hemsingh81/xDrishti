using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Assistant;
using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Tests.Assistant;

/// <summary>Spec P9-05 AC 6: the assistant's portfolio figures are the Holdings screen's calculator over the rows of the views, with hand-worked numbers.</summary>
public sealed class PortfolioFiguresTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    private static PortfolioHoldingRow Row(string symbol, long qty, decimal cost, decimal last, string acquired, string account = "Main", string? sector = null, string[]? baskets = null, string[]? buckets = null) =>
        new(account, symbol, qty, cost, last, null, "open", sector, buckets ?? ["Core"], baskets ?? [], [new PortfolioLotRow(DateOnly.Parse(acquired, System.Globalization.CultureInfo.InvariantCulture), qty, cost, null, null, false)]);

    private static PortfolioHoldingRow[] Fixture => [Row("X", 10, 100m, 110m, "2025-01-01", sector: "IT", baskets: ["Alpha"]), Row("Y", 5, 200m, 180m, "2025-06-01", account: "Second", sector: "Banks", baskets: ["Alpha", "Beta"])];

    [Fact]
    public void The_totals_are_invested_value_unrealised_and_percent_worked_by_hand()
    {
        var (groups, nonAdditive) = PortfolioFigures.Compute(Fixture, PortfolioFigures.None, Today);

        nonAdditive.ShouldBeFalse();
        var all = groups.Single();
        all["group"]!.GetValue<string>().ShouldBe("all");
        all["invested"]!["number"]!.GetValue<decimal>().ShouldBe(2000m);
        all["value"]!["number"]!.GetValue<decimal>().ShouldBe(2000m);
        all["unrealised"]!["number"]!.GetValue<decimal>().ShouldBe(0m);
        all["unrealisedPct"]!["number"]!.GetValue<decimal>().ShouldBe(0m);
        all["xirr"]!["state"]!.GetValue<string>().ShouldBe("value");
    }

    [Fact]
    public void Each_holding_is_a_group_of_its_own_by_account_with_the_screens_rounding()
    {
        var (groups, _) = PortfolioFigures.Compute(Fixture, PortfolioFigures.Account, Today);

        groups.Select(g => g["group"]!.GetValue<string>()).ShouldBe(["Main", "Second"]);
        groups[0]["unrealised"]!["number"]!.GetValue<decimal>().ShouldBe(100m);
        groups[0]["unrealisedPct"]!["number"]!.GetValue<decimal>().ShouldBe(10m);
        groups[1]["unrealised"]!["number"]!.GetValue<decimal>().ShouldBe(-100m);
        groups[1]["unrealisedPct"]!["number"]!.GetValue<decimal>().ShouldBe(-10m);
    }

    [Fact]
    public void A_holding_in_two_baskets_makes_the_groups_non_additive()
    {
        var (groups, nonAdditive) = PortfolioFigures.Compute(Fixture, PortfolioFigures.Basket, Today);

        nonAdditive.ShouldBeTrue();
        groups.Select(g => g["group"]!.GetValue<string>()).ShouldBe(["Alpha", "Beta"]);
        groups[0]["holdings"]!.GetValue<int>().ShouldBe(2);
    }

    [Fact]
    public void Sector_and_bucket_groups_name_the_unclassified()
    {
        var rows = new[] { Row("X", 1, 100m, 100m, "2025-01-01", sector: null, buckets: []) };

        PortfolioFigures.Compute(rows, PortfolioFigures.Sector, Today).Groups.Single()["group"]!.GetValue<string>().ShouldBe("unclassified");
        PortfolioFigures.Compute(rows, PortfolioFigures.Bucket, Today).Groups.Single()["group"]!.GetValue<string>().ShouldBe("unclassified");
    }

    [Fact]
    public void A_holding_held_under_the_minimum_days_gives_insufficient_not_a_rate()
    {
        var rows = new[] { Row("X", 10, 100m, 110m, "2026-10-10") };

        var group = PortfolioFigures.Compute(rows, PortfolioFigures.None, Today).Groups.Single();

        group["xirr"]!["state"]!.GetValue<string>().ShouldBe("insufficient");
        group["xirr"]!["number"].ShouldBeNull();
        (Today.DayNumber - new DateOnly(2026, 10, 10).DayNumber).ShouldBeLessThan(Xirr.MinDays);
    }

    [Fact]
    public void A_holding_with_no_last_price_has_no_value_profit_or_rate_and_is_counted()
    {
        var rows = new[] { Row("X", 10, 100m, 0m, "2025-01-01"), Row("Y", 5, 200m, 180m, "2025-06-01") };

        var group = PortfolioFigures.Compute(rows, PortfolioFigures.None, Today).Groups.Single();

        group["unpricedHoldings"]!.GetValue<int>().ShouldBe(1);
        group["invested"]!["number"]!.GetValue<decimal>().ShouldBe(1000m);
        group["note"]!.GetValue<string>().ShouldContain("no usable last price");

        var alone = PortfolioFigures.Compute([Row("X", 10, 100m, 0m, "2025-01-01")], PortfolioFigures.None, Today).Groups.Single();
        alone["value"]!["state"]!.GetValue<string>().ShouldBe("unavailable");
        alone["value"]!["number"].ShouldBeNull();
        alone["xirr"]!["state"]!.GetValue<string>().ShouldBe("unavailable");
    }

    [Fact]
    public void No_holding_gives_no_group_and_no_exception()
    {
        PortfolioFigures.Compute([], PortfolioFigures.Account, Today).Groups.ShouldBeEmpty();
    }

    [Fact]
    public void The_figures_equal_the_calculator_of_the_holdings_screen()
    {
        var data = new HoldingData(Guid.Empty, Guid.Empty, "Main", Guid.Empty, "", null, null, "", "", null, "X", "X", 10, 100m, 110m, null, "open", DateTimeOffset.MinValue, null, DateTimeOffset.MinValue, [new LotData(Guid.Empty, new DateOnly(2025, 1, 1), 10, 100m, "import", false, null, null, false)]);
        var screen = ReturnsCalculator.ForGroup([data], Today);

        var group = PortfolioFigures.Compute([Row("X", 10, 100m, 110m, "2025-01-01")], PortfolioFigures.None, Today).Groups.Single();

        group["value"]!["number"]!.GetValue<decimal>().ShouldBe(screen.Value!.Value);
        group["unrealisedPct"]!["number"]!.GetValue<decimal>().ShouldBe(screen.PnlPct!.Value);
        group["xirr"]!["number"]!.GetValue<double>().ShouldBe(Math.Round(screen.Xirr.Rate!.Value * 100.0, 2, MidpointRounding.AwayFromZero));
    }
}
