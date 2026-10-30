using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests.Confirm;

/// <summary>Spec P12-08 Data: the three confirm settings are in the registry with their defaults, ranges and risk direction (each riskier when raised), agree with the shared rule the API reads, and reach the issuer through the cache.</summary>
public sealed class ConfirmSettingsTests
{
    [Theory]
    [InlineData(ConfirmSettings.TtlKey, "90", "15", "300")]
    [InlineData(ConfirmSettings.DriftKey, "0.30", "0.05", "1.00")]
    [InlineData(ConfirmSettings.StepUpKey, "10", "1", "100")]
    public void Each_confirm_setting_is_registered_with_its_range_and_is_riskier_when_raised(string key, string def, string min, string max)
    {
        ExecSettingRegistry.Find(key).ShouldBe(new ExecSetting(key, ExecSettingKind.Number, def, min, max, ExecRiskDirection.HigherIsRiskier));
    }

    [Fact]
    public void The_registry_and_the_shared_rule_agree_on_defaults_and_ranges()
    {
        var defaults = ExecSettingRegistry.All.ToDictionary(s => s.Key, s => s.Default);
        ConfirmSettings.FromValues(defaults).ShouldBe(ConfirmSettings.Default);
        ExecSettingRegistry.Find(ConfirmSettings.FeedMaxAgeKey)!.ShouldSatisfyAllConditions(
            s => decimal.Parse(s.Min, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(ConfirmSettings.FeedMaxAgeRange.Min),
            s => decimal.Parse(s.Max, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(ConfirmSettings.FeedMaxAgeRange.Max));
        foreach (var (key, range) in new[] { (ConfirmSettings.TtlKey, ConfirmSettings.TtlRange), (ConfirmSettings.DriftKey, ConfirmSettings.DriftRange), (ConfirmSettings.StepUpKey, ConfirmSettings.StepUpRange) })
        {
            var s = ExecSettingRegistry.Find(key)!;
            (decimal.Parse(s.Min, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(s.Max, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(range, key);
        }
    }

    [Fact]
    public void The_cache_serves_the_confirm_settings_of_the_newest_read_and_none_before_it()
    {
        var cache = new ExecSettingsCache(TimeProvider.System);
        ((IConfirmSettings)cache).Current().ShouldBeNull();
        var values = ExecSettingRegistry.All.ToDictionary(s => s.Key, s => s.Default);
        values[ConfirmSettings.TtlKey] = "120";

        cache.Set(new ExecSettingsView(values, []));

        ((IConfirmSettings)cache).Current().ShouldBe(ConfirmSettings.Default with { TtlSeconds = 120 });
    }
}
