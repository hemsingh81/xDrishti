using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class RiskProfileTests
{
    private static RiskLimits Limits => AccountData.StandardLimits();

    private static IEnumerable<string> Fields(RiskLimits limits) => limits.Validate().Items.Select(i => i.Field);

    [Fact]
    public void The_documented_standard_values_are_valid() =>
        Limits.Validate().HasErrors.ShouldBeFalse();

    [Fact]
    public void Create_stores_the_name_and_values()
    {
        var profile = RiskProfile.Create("  Aggressive  ", Limits with { RiskPerTradePct = 1m }, AccountData.T0).Value;

        profile.Name.ShouldBe("Aggressive");
        profile.RiskPerTradePct.ShouldBe(1m);
        profile.IsSeeded.ShouldBeFalse();
        profile.Limits.ShouldBe(Limits with { RiskPerTradePct = 1m });
    }

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(2.5, false)]
    public void Risk_per_trade_is_above_zero_and_at_most_two(double value, bool valid) =>
        Fields(Limits with { RiskPerTradePct = (decimal)value }).Contains("riskPerTradePct").ShouldBe(!valid);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Counts_are_between_one_and_one_hundred(int value, bool valid)
    {
        Fields(Limits with { MaxNewEntriesPerDay = value }).Contains("maxNewEntriesPerDay").ShouldBe(!valid);
        Fields(Limits with { MaxOpenPositions = value }).Contains("maxOpenPositions").ShouldBe(!valid);
        Fields(Limits with { MaxPerSector = value }).Contains("maxPerSector").ShouldBe(!valid);
        Fields(Limits with { MaxIntraday = value }).Contains("maxIntraday").ShouldBe(!valid);
        Fields(Limits with { MaxSwing = value }).Contains("maxSwing").ShouldBe(!valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.5, true)]
    [InlineData(100, true)]
    [InlineData(100.1, false)]
    public void Daily_and_weekly_loss_limits_must_be_above_zero(double value, bool valid)
    {
        Fields(Limits with { DailyLossR = (decimal)value }).Contains("dailyLossR").ShouldBe(!valid);
        Fields(Limits with { DailyLossPct = (decimal)value }).Contains("dailyLossPct").ShouldBe(!valid);
        Fields(Limits with { WeeklyLossR = (decimal)value }).Contains("weeklyLossR").ShouldBe(!valid);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(-0.1, false)]
    [InlineData(1.1, false)]
    public void Pair_correlation_is_between_zero_and_one(double value, bool valid) =>
        Fields(Limits with { MaxPairCorrelation = (decimal)value }).Contains("maxPairCorrelation").ShouldBe(!valid);

    [Fact]
    public void Total_risk_cannot_be_below_the_risk_of_a_single_trade() =>
        Fields(Limits with { RiskPerTradePct = 1m, MaxTotalRiskPct = 0.5m }).ShouldContain("maxTotalRiskPct");

    [Fact]
    public void An_unknown_weekly_loss_action_is_rejected() =>
        Fields(Limits with { WeeklyLossAction = (WeeklyLossAction)42 }).ShouldContain("weeklyLossAction");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_profile_needs_a_name(string? name)
    {
        var result = RiskProfile.Create(name, Limits, AccountData.T0);

        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
        result.Error.Fields!.ShouldContain(f => f.Field == "name");
    }

    [Fact]
    public void Update_replaces_name_and_values_and_an_invalid_update_changes_nothing()
    {
        var profile = RiskProfile.Create("Mine", Limits, AccountData.T0).Value;

        var bad = profile.Update("Renamed", Limits with { RiskPerTradePct = 5m }, AccountData.T0.AddHours(1));
        var good = profile.Update("Renamed", Limits with { MaxOpenPositions = 7 }, AccountData.T0.AddHours(2));

        bad.IsFailure.ShouldBeTrue();
        good.IsSuccess.ShouldBeTrue();
        profile.Name.ShouldBe("Renamed");
        profile.RiskPerTradePct.ShouldBe(0.5m);
        profile.MaxOpenPositions.ShouldBe(7);
        profile.UpdatedAt.ShouldBe(AccountData.T0.AddHours(2));
    }

    [Fact]
    public void A_profile_in_use_cannot_be_deleted()
    {
        var profile = RiskProfile.Create("Mine", Limits, AccountData.T0).Value;

        var result = profile.CheckCanBeDeleted(2);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        result.Error.Message.ShouldContain("2 account");
    }

    [Fact]
    public void An_unused_profile_can_be_deleted() =>
        RiskProfile.Create("Mine", Limits, AccountData.T0).Value.CheckCanBeDeleted(0).IsSuccess.ShouldBeTrue();
}
