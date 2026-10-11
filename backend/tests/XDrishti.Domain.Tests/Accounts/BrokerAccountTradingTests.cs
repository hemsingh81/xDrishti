using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class BrokerAccountTradingTests
{
    private static TradingSpec With(
        TradingMode? mode = null,
        Guid? profile = null,
        CapitalSource? source = null,
        decimal? amount = null,
        bool? intraday = null,
        bool? swing = null,
        Instruments? intradayInstruments = null,
        Instruments? swingBuy = null,
        Instruments? swingSell = null,
        TradingOverrides? overrides = null)
    {
        var basis = AccountData.CompleteTrading();
        return new TradingSpec(
            mode ?? basis.Mode,
            profile ?? basis.RiskProfileId,
            source ?? basis.CapitalSource,
            amount ?? basis.CapitalAmount,
            intraday ?? basis.Intraday,
            swing ?? basis.Swing,
            intradayInstruments ?? basis.IntradayInstruments,
            swingBuy ?? basis.SwingBuyInstruments,
            swingSell ?? basis.SwingSellInstruments,
            overrides ?? basis.Overrides);
    }

    private static BrokerAccount EnabledTrading()
    {
        var account = AccountData.New();
        account.ConfigureTrading(AccountData.CompleteTrading(), AccountData.T0).IsSuccess.ShouldBeTrue();
        account.ChangeRoles(new RolesChange(null, true, null), null, null, AccountData.T0).IsSuccess.ShouldBeTrue();
        return account;
    }

    private static IEnumerable<string> Fields(Common.Result result) => result.Error.Fields!.Select(f => f.Field);

    [Fact]
    public void A_complete_configuration_is_stored()
    {
        var account = AccountData.New();
        var spec = With(
            mode: TradingMode.ManualLive,
            swing: true,
            swingBuy: Instruments.EquityDelivery | Instruments.Futures,
            swingSell: Instruments.Futures,
            overrides: new TradingOverrides(0.25m, 2, 3, 1.0m));

        account.ConfigureTrading(spec, AccountData.T0.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        account.Trading.Mode.ShouldBe(TradingMode.ManualLive);
        account.Trading.Intraday.ShouldBeTrue();
        account.Trading.Swing.ShouldBeTrue();
        account.Trading.SwingBuyInstruments.ShouldBe(Instruments.EquityDelivery | Instruments.Futures);
        account.Trading.Overrides.ShouldBe(new TradingOverrides(0.25m, 2, 3, 1.0m));
        account.UpdatedAt.ShouldBe(AccountData.T0.AddMinutes(5));
    }

    [Fact]
    public void While_the_role_is_off_an_incomplete_configuration_is_accepted()
    {
        var account = AccountData.New();
        var empty = With(profile: null, amount: null, intraday: false, intradayInstruments: Instruments.None);
        var spec = empty with { RiskProfileId = null, CapitalAmount = null };

        account.ConfigureTrading(spec, AccountData.T0).IsSuccess.ShouldBeTrue();

        account.Trading.RiskProfileId.ShouldBeNull();
    }

    [Fact]
    public void While_the_role_is_on_the_configuration_must_stay_complete()
    {
        var account = EnabledTrading();

        var result = account.ConfigureTrading(AccountData.CompleteTrading() with { RiskProfileId = null, CapitalAmount = null, Intraday = false }, AccountData.T0);

        result.IsFailure.ShouldBeTrue();
        Fields(result).ShouldBe(["riskProfileId", "capitalAmount", "styles"], ignoreOrder: true);
        account.Trading.RiskProfileId.ShouldBe(AccountData.ProfileId); // unchanged
    }

    [Fact]
    public void Broker_funds_need_no_amount()
    {
        var account = EnabledTrading();

        account.ConfigureTrading(AccountData.CompleteTrading() with { CapitalSource = CapitalSource.BrokerFunds, CapitalAmount = null }, AccountData.T0).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Capital_must_be_more_than_zero(double amount) =>
        Fields(AccountData.New().ConfigureTrading(With(amount: (decimal)amount), AccountData.T0)).ShouldContain("capitalAmount");

    [Fact]
    public void Intraday_needs_instruments_when_the_role_is_on()
    {
        var account = EnabledTrading();

        var result = account.ConfigureTrading(With(intradayInstruments: Instruments.None), AccountData.T0);

        Fields(result).ShouldContain("instruments.intraday");
    }

    [Fact]
    public void Swing_needs_buy_instruments_but_sell_may_stay_empty()
    {
        var account = EnabledTrading();

        var missing = account.ConfigureTrading(With(swing: true), AccountData.T0);
        var ok = account.ConfigureTrading(With(swing: true, swingBuy: Instruments.EquityDelivery), AccountData.T0);

        Fields(missing).ShouldContain("instruments.swingBuy");
        ok.IsSuccess.ShouldBeTrue();
        account.Trading.SwingSellInstruments.ShouldBe(Instruments.None);
    }

    [Theory]
    [InlineData(Instruments.EquityDelivery, "instruments.intraday")]
    [InlineData(Instruments.EquityIntraday | Instruments.EquityDelivery, "instruments.intraday")]
    public void Intraday_allows_only_equity_intraday_and_futures(Instruments chosen, string field) =>
        Fields(AccountData.New().ConfigureTrading(With(intradayInstruments: chosen), AccountData.T0)).ShouldContain(field);

    [Fact]
    public void Swing_buy_does_not_allow_intraday_equity_and_swing_sell_allows_only_futures()
    {
        var result = AccountData.New().ConfigureTrading(
            With(swing: true, swingBuy: Instruments.EquityIntraday, swingSell: Instruments.EquityDelivery), AccountData.T0);

        Fields(result).ShouldBe(["instruments.swingBuy", "instruments.swingSell"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(2.01, false)]
    [InlineData(-1, false)]
    public void The_risk_per_trade_override_is_above_zero_and_at_most_two_percent(double value, bool valid)
    {
        var spec = With(overrides: new TradingOverrides((decimal)value, null, null, null));

        AccountData.New().ConfigureTrading(spec, AccountData.T0).IsSuccess.ShouldBe(valid);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    public void The_count_overrides_are_between_one_and_one_hundred(int value, bool valid)
    {
        AccountData.New().ConfigureTrading(With(overrides: new TradingOverrides(null, value, null, null)), AccountData.T0).IsSuccess.ShouldBe(valid);
        AccountData.New().ConfigureTrading(With(overrides: new TradingOverrides(null, null, value, null)), AccountData.T0).IsSuccess.ShouldBe(valid);
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(100.5, false)]
    public void The_daily_loss_override_must_be_above_zero(double value, bool valid) =>
        AccountData.New().ConfigureTrading(With(overrides: new TradingOverrides(null, null, null, (decimal)value)), AccountData.T0).IsSuccess.ShouldBe(valid);

    [Fact]
    public void Unknown_modes_and_capital_sources_are_rejected()
    {
        var result = AccountData.New().ConfigureTrading(With(mode: (TradingMode)9, source: (CapitalSource)9), AccountData.T0);

        Fields(result).ShouldBe(["mode", "capitalSource"], ignoreOrder: true);
    }

    [Fact]
    public void Switching_the_role_off_keeps_every_setting()
    {
        var account = EnabledTrading();

        account.ChangeRoles(new RolesChange(null, false, null), null, null, AccountData.T0).IsSuccess.ShouldBeTrue();

        account.Trading.Enabled.ShouldBeFalse();
        account.Trading.ToSpec().ShouldBe(AccountData.CompleteTrading());
    }
}
