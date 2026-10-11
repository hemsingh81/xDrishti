using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class BrokerAccountPortfolioDataTests
{
    private static readonly Guid Growth = Guid.Parse("00000000-0000-7000-8000-0000000000c1");

    [Fact]
    public void Portfolio_settings_are_stored_and_the_role_can_be_switched_on_at_any_time()
    {
        var account = AccountData.New();

        account.ConfigurePortfolio(new PortfolioSpec(Growth, false), AccountData.T0);
        account.ChangeRoles(new RolesChange(null, null, true), null, null, AccountData.T0).IsSuccess.ShouldBeTrue();

        account.Portfolio.DefaultBucketId.ShouldBe(Growth);
        account.Portfolio.IncludeInTotals.ShouldBeFalse();
        account.Portfolio.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void The_role_can_be_switched_on_without_any_portfolio_settings()
    {
        var account = AccountData.New();

        account.ChangeRoles(new RolesChange(null, null, true), null, null, AccountData.T0).IsSuccess.ShouldBeTrue();

        account.Portfolio.Enabled.ShouldBeTrue();
        account.Portfolio.DefaultBucketId.ShouldBeNull(); // null: the account's own default bucket
        account.Portfolio.IncludeInTotals.ShouldBeTrue();
    }

    [Fact]
    public void No_chosen_bucket_means_the_accounts_own()
    {
        var account = AccountData.New();
        account.ConfigurePortfolio(new PortfolioSpec(Growth, true), AccountData.T0);

        account.ConfigurePortfolio(new PortfolioSpec(null, true), AccountData.T0);

        account.Portfolio.DefaultBucketId.ShouldBeNull();
    }

    [Fact]
    public void Settings_are_kept_when_the_role_is_switched_off()
    {
        var account = AccountData.New();
        account.ConfigurePortfolio(new PortfolioSpec(Growth, false), AccountData.T0);
        account.ChangeRoles(new RolesChange(null, null, true), null, null, AccountData.T0);

        account.ChangeRoles(new RolesChange(null, null, false), null, null, AccountData.T0);

        account.Portfolio.Enabled.ShouldBeFalse();
        account.Portfolio.DefaultBucketId.ShouldBe(Growth);
        account.Portfolio.IncludeInTotals.ShouldBeFalse();
    }

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
