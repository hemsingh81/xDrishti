using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class BrokerAccountPortfolioDataTests
{
    private static readonly DateOnly Today = new(2026, 10, 10);

    [Fact]
    public void Portfolio_settings_are_stored_and_the_role_can_then_be_switched_on()
    {
        var account = AccountData.New();

        account.ConfigurePortfolio(new PortfolioSpec(" Trading ", new DateOnly(2024, 4, 1), false), Today, AccountData.T0).IsSuccess.ShouldBeTrue();
        account.ChangeRoles(new RolesChange(null, null, true), null, null, AccountData.T0).IsSuccess.ShouldBeTrue();

        account.Portfolio.DefaultBucket.ShouldBe("Trading");
        account.Portfolio.HistoryFrom.ShouldBe(new DateOnly(2024, 4, 1));
        account.Portfolio.IncludeInTotals.ShouldBeFalse();
        account.Portfolio.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void An_enabled_portfolio_cannot_lose_its_bucket()
    {
        var account = AccountData.New();
        account.ConfigurePortfolio(new PortfolioSpec("Trading", null, true), Today, AccountData.T0);
        account.ChangeRoles(new RolesChange(null, null, true), null, null, AccountData.T0);

        var result = account.ConfigurePortfolio(new PortfolioSpec(null, null, true), Today, AccountData.T0);

        result.Error.Fields!.ShouldContain(f => f.Field == "defaultBucket");
        account.Portfolio.DefaultBucket.ShouldBe("Trading");
    }

    [Theory]
    [InlineData("2000-01-01", true)]
    [InlineData("2026-10-10", true)]
    [InlineData("1999-12-31", false)]
    [InlineData("2026-10-11", false)]
    public void The_history_start_is_between_2000_and_today(string date, bool valid) =>
        AccountData.New().ConfigurePortfolio(new PortfolioSpec("Trading", DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), true), Today, AccountData.T0).IsSuccess.ShouldBe(valid);

    [Theory]
    [InlineData("My Choice", true)]
    [InlineData("a_b-c", true)]
    [InlineData("a/b", false)]
    public void The_bucket_name_must_be_well_formed(string name, bool valid) =>
        AccountData.New().ConfigurePortfolio(new PortfolioSpec(name, null, true), Today, AccountData.T0).IsSuccess.ShouldBe(valid);

    [Theory]
    [InlineData(0, true)]
    [InlineData(5000, true)]
    [InlineData(-1, false)]
    [InlineData(5001, false)]
    public void The_live_feed_budget_is_between_zero_and_five_thousand(int budget, bool valid)
    {
        var account = AccountData.New();

        account.ConfigureData(budget, AccountData.T0).IsSuccess.ShouldBe(valid);

        account.LiveFeedBudget.ShouldBe(valid ? budget : BrokerAccount.DefaultLiveFeedBudget);
    }

    [Fact]
    public void A_missing_live_feed_budget_is_rejected() =>
        AccountData.New().ConfigureData(null, AccountData.T0).Error.Fields!.ShouldContain(f => f.Field == "liveFeedBudget");
}
