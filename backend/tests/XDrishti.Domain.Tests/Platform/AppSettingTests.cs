using XDrishti.Domain.Platform;

namespace XDrishti.Domain.Tests.Platform;

public sealed class AppSettingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_setting_keeps_its_key_value_actor_and_time()
    {
        var setting = AppSetting.Create(AppSettingKeys.PortfolioBenchmarkInstrument, "abc", "owner@example.invalid", Now);

        (setting.Id, setting.Value, setting.UpdatedBy, setting.UpdatedAt).ShouldBe((AppSettingKeys.PortfolioBenchmarkInstrument, "abc", "owner@example.invalid", Now));
    }

    [Fact]
    public void A_change_overwrites_the_value_and_who_and_when()
    {
        var setting = AppSetting.Create("k", "one", "a@example.invalid", Now);

        setting.Change("two", "b@example.invalid", Now.AddHours(1));

        (setting.Value, setting.UpdatedBy, setting.UpdatedAt).ShouldBe(("two", "b@example.invalid", Now.AddHours(1)));
    }

    [Fact]
    public void A_missing_actor_is_recorded_as_unknown_and_a_long_one_is_cut()
    {
        AppSetting.Create("k", "v", " ", Now).UpdatedBy.ShouldBe("unknown");
        AppSetting.Create("k", "v", new string('x', 400), Now).UpdatedBy.Length.ShouldBe(AppSetting.MaxUpdatedByLength);
    }

    [Theory]
    [InlineData("", "v")]
    [InlineData(" ", "v")]
    [InlineData("k", "")]
    public void An_empty_key_or_value_is_refused(string key, string value)
    {
        Should.Throw<ArgumentException>(() => AppSetting.Create(key, value, "a", Now));
    }

    [Fact]
    public void A_key_or_value_that_is_too_long_is_refused()
    {
        Should.Throw<ArgumentException>(() => AppSetting.Create(new string('k', AppSetting.MaxKeyLength + 1), "v", "a", Now));
        Should.Throw<ArgumentException>(() => AppSetting.Create("k", new string('v', AppSetting.MaxValueLength + 1), "a", Now));
        Should.Throw<ArgumentException>(() => AppSetting.Create("k", "v", "a", Now).Change(new string('v', AppSetting.MaxValueLength + 1), "a", Now));
    }

    [Fact]
    public void The_value_holds_8000_characters_for_a_charge_sheet_and_the_key_prefix_is_the_research_one()
    {
        AppSetting.MaxValueLength.ShouldBe(8000);
        AppSetting.Create("k", new string('v', 7_999), "a", Now).Value.Length.ShouldBe(7_999);
        AppSetting.Create("k", new string('v', 8_000), "a", Now).Value.Length.ShouldBe(8_000);
        Should.Throw<ArgumentException>(() => AppSetting.Create("k", new string('v', 8_001), "a", Now));
        AppSettingKeys.CostSheetPrefix.ShouldBe("research.cost_sheet.");
        (AppSettingKeys.CostSheetPrefix + new string('a', 40)).Length.ShouldBeLessThanOrEqualTo(AppSetting.MaxKeyLength);
    }
}
