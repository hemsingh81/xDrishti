using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>How a Trading account becomes the plan's account (spec P7-09): profile and overrides, fixed versus broker-funds capital, holdings by account, and the open-book setting.</summary>
public sealed class StoredPlanContextAccountTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 10, 10);

    private static readonly RiskLimits Limits = new(0.5m, 25m, 3, 5, 3.0m, 2, 0.70m, 2, 2, 2m, 1.5m, 5m, WeeklyLossAction.HalveSize);

    private static RiskProfile Profile() => RiskProfile.Create("Std", Limits, T0).Value;

    private static BrokerAccount Account(RiskProfile profile, CapitalSource source, decimal? amount, TradingMode mode = TradingMode.Paper, TradingOverrides? overrides = null, bool portfolio = false, string client = "1000000001")
    {
        var account = BrokerAccount.Create("Acct " + client, client, null, null, null, Today, T0).Value;
        account.ConfigureTrading(new TradingSpec(mode, profile.Id, source, amount, true, false, Instruments.EquityIntraday, Instruments.None, Instruments.None, overrides ?? TradingOverrides.None), T0).IsSuccess.ShouldBeTrue();
        account.ChangeRoles(new RolesChange(null, true, portfolio ? true : null), null, null, T0).IsSuccess.ShouldBeTrue();
        return account;
    }

    private static HoldingData Holding(Guid account, string symbol) => new(
        Guid.NewGuid(), account, "n", Guid.NewGuid(), "b", Guid.NewGuid(), symbol, "NSE", "1", null, symbol, symbol, 10, 1m, 1m, null, "open", T0, null, T0, []);

    [Fact]
    public void A_fixed_capital_account_takes_the_profile_and_the_book_is_unavailable_by_default()
    {
        var profile = Profile();
        var account = Account(profile, CapitalSource.Fixed, 500_000m);

        var a = StoredPlanContextSource.ToAccount(account, [], new() { [profile.Id] = profile }, bookAssumedEmpty: false);

        (a.Equity, a.FundsAvailable, a.RiskPerTradePct, a.MaxCapitalPerTradePct, a.MaxTotalRiskPct, a.MaxPerSector, a.DailyLossLimitPct).ShouldBe((500_000d, 500_000d, 0.5, 25d, 3d, 2, 1.5));
        (a.OpenRiskPct, a.DailyLossPct).ShouldBe((null, null)); // not known unless the owner says the book is empty
        a.Mode.ShouldBe(TradingMode.Paper);
    }

    [Fact]
    public void With_the_owner_setting_the_open_risk_and_the_loss_today_are_zero()
    {
        var profile = Profile();

        var a = StoredPlanContextSource.ToAccount(Account(profile, CapitalSource.Fixed, 500_000m), [], new() { [profile.Id] = profile }, bookAssumedEmpty: true);

        (a.OpenRiskPct, a.DailyLossPct).ShouldBe((0d, 0d));
    }

    [Fact]
    public void Overrides_beat_the_profile_for_risk_and_daily_loss()
    {
        var profile = Profile();
        var account = Account(profile, CapitalSource.Fixed, 500_000m, overrides: new TradingOverrides(0.25m, null, null, 1.0m));

        var a = StoredPlanContextSource.ToAccount(account, [], new() { [profile.Id] = profile }, true);

        (a.RiskPerTradePct, a.DailyLossLimitPct).ShouldBe((0.25, 1.0));
    }

    [Fact]
    public void A_broker_funds_account_has_no_equity_and_a_missing_profile_has_no_limits()
    {
        var profile = Profile();
        var account = Account(profile, CapitalSource.BrokerFunds, null, TradingMode.ManualLive);

        var a = StoredPlanContextSource.ToAccount(account, [], [], true);

        a.Mode.ShouldBe(TradingMode.ManualLive);
        (a.Equity, a.FundsAvailable, a.RiskPerTradePct, a.MaxTotalRiskPct).ShouldBe((null, null, null, null));
    }

    [Fact]
    public void Holdings_are_taken_by_account_only_for_an_account_with_the_portfolio_role()
    {
        var profile = Profile();
        var withRole = Account(profile, CapitalSource.Fixed, 500_000m, portfolio: true, client: "1000000002");
        var without = Account(profile, CapitalSource.Fixed, 500_000m, client: "1000000003");
        var held = new List<HoldingData> { Holding(withRole.Id, "nse:aaa"), Holding(Guid.NewGuid(), "NSE:BBB") };
        var profiles = new Dictionary<Guid, RiskProfile> { [profile.Id] = profile };

        var a = StoredPlanContextSource.ToAccount(withRole, held, profiles, true);
        var b = StoredPlanContextSource.ToAccount(without, held, profiles, true);

        a.HeldSymbols.ShouldNotBeNull().ShouldBe(["NSE:AAA"]);
        b.HeldSymbols.ShouldNotBeNull().ShouldBeEmpty();
    }
}
