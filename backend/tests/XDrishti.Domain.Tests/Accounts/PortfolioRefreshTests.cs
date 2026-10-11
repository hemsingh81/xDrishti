using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

/// <summary>The automatic holdings refresh and first import date of an account (ADR 0012, 0013): off by default, off or daily, the date defaults to 2025-01-01.</summary>
public sealed class PortfolioRefreshTests
{
    [Fact]
    public void A_new_account_has_the_refresh_off_at_16_00_and_no_chosen_first_import_date()
    {
        var account = AccountData.New();

        account.Portfolio.Refresh.ShouldBe(new PortfolioRefresh(PortfolioRefreshMode.Off, new TimeOnly(16, 0)));
        account.Portfolio.Refresh.IsOn.ShouldBeFalse();
        account.Portfolio.FirstImportDate.ShouldBeNull();
        account.Portfolio.ImportDate.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void Off_and_daily_are_accepted()
    {
        PortfolioRefresh.Create(PortfolioRefreshMode.Off, new TimeOnly(16, 0)).IsSuccess.ShouldBeTrue();
        PortfolioRefresh.Create(PortfolioRefreshMode.Daily, new TimeOnly(16, 0)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_interval_mode_is_gone()
    {
        Enum.GetNames<PortfolioRefreshMode>().ShouldBe(["Off", "Daily"]);
        PortfolioRefresh.Create((PortfolioRefreshMode)2, new TimeOnly(16, 0)).Error.Fields!.Single().Field.ShouldBe("refreshMode");
    }

    [Fact]
    public void An_undefined_mode_is_refused_and_seconds_of_the_time_are_dropped()
    {
        PortfolioRefresh.Create((PortfolioRefreshMode)9, new TimeOnly(16, 0)).Error.Fields!.Single().Field.ShouldBe("refreshMode");

        var created = PortfolioRefresh.Create(PortfolioRefreshMode.Daily, new TimeOnly(9, 30, 45)).Value;

        created.DailyTimeIst.ShouldBe(new TimeOnly(9, 30));
    }

    [Fact]
    public void Configuring_the_portfolio_stores_the_refresh_and_the_date_and_leaves_them_alone_when_none_is_given()
    {
        var account = AccountData.New();
        var daily = new PortfolioRefresh(PortfolioRefreshMode.Daily, new TimeOnly(17, 45));

        account.ConfigurePortfolio(new PortfolioSpec(null, true, daily, new DateOnly(2024, 4, 1)), AccountData.T0);
        account.ConfigurePortfolio(new PortfolioSpec(null, false), AccountData.T0);

        account.Portfolio.Refresh.ShouldBe(daily);
        account.Portfolio.FirstImportDate.ShouldBe(new DateOnly(2024, 4, 1));
        account.Portfolio.ImportDate.ShouldBe(new DateOnly(2024, 4, 1));
        account.Portfolio.IncludeInTotals.ShouldBeFalse();
    }
}
