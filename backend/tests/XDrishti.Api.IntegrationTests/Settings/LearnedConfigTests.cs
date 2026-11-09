using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDrishti.Api.Confidence;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Settings;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.LearnedSettings;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Api.IntegrationTests.Settings;

/// <summary>
/// AU-10: the allow-list (every key has a verified direction, Loop:Level is not on it), the whole-candidate check with the real validators (criterion 10: the four pairs), and the start-up overlay that must never stop the host.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LearnedConfigTests(ApiFactory factory)
{
    private const string TierA = "Confidence:CostStress:TierACutCrore";
    private const string TierB = "Confidence:CostStress:TierBCutCrore";

    private ILearnedConfig Config => factory.Services.GetRequiredService<ILearnedConfig>();

    private static Dictionary<string, string> With(string key, string value) => new() { [key] = value };

    [Fact]
    public void Every_allow_listed_key_has_a_descriptor_with_a_declared_direction_and_loop_level_is_not_on_it()
    {
        foreach (var key in LearnedAllowList.Keys)
        {
            var d = ConfidenceSettingDescriptors.All.Single(x => x.Key == key);
            d.Safer.ShouldNotBe(SettingSafer.None, key);
            d.Learnable.ShouldBeTrue(key);
            d.HasRange.ShouldBeTrue(key);
        }

        LearnedAllowList.Keys.ShouldNotContain("Confidence:Loop:Level");
        ConfidenceSettingDescriptors.All.Where(d => d.Safer == SettingSafer.None).ShouldAllBe(d => !d.Learnable);
        SettingsRegistry.All.ShouldAllBe(d => !d.Learnable); // the stored switches (Auto and the rest) are never learnable
    }

    [Fact]
    public void The_configured_value_is_the_default_when_nothing_is_set()
    {
        Config.ConfiguredValue("Confidence:CostStress:TierABps").ShouldBe("3");
        Config.ConfiguredValue("Nope:Key").ShouldBeNull();
        Config.Descriptor("Confidence:CostStress:TierABps")!.Learnable.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Confidence:Ladder:MinProbability", "0.99")] // the hold-out bar (0.95) would be below the ladder bar
    [InlineData("Confidence:CostStress:MidMultiplier", "5")] // high (2) would not be above mid
    [InlineData(TierB, "100")] // B must be below A (100)
    [InlineData("Confidence:Readiness:BuildingFrom", "90")] // building (40..) must stay below usable (70) and ready (85)
    public void A_change_that_is_safe_alone_but_breaks_its_pair_is_refused_as_a_whole(string key, string value)
    {
        Config.Failures(With(key, value)).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(TierB, "99")]
    [InlineData("Confidence:CostStress:TierABps", "4.4")]
    [InlineData("Confidence:Readiness:BuildingFrom", "60")]
    public void A_change_inside_its_pair_passes(string key, string value)
    {
        Config.Failures(With(key, value)).ShouldBeEmpty();
    }

    [Fact]
    public void A_value_outside_the_bounds_or_a_fraction_for_a_whole_number_is_refused()
    {
        Config.Failures(With("Confidence:CostStress:TierABps", "9")).ShouldNotBeEmpty();
        Config.Failures(With("Confidence:Readiness:MinTradesPerCell", "30.5")).ShouldNotBeEmpty();
        Config.Failures(With("Confidence:Nope:Key", "1")).ShouldNotBeEmpty();
    }

    private sealed class FixedSource(IReadOnlyList<LearnedOverride>? applied) : ILearnedOverrideSource
    {
        public IReadOnlyList<LearnedOverride>? ReadApplied() => applied;
    }

    private ConfidenceSettings Overlay(IReadOnlyList<LearnedOverride>? applied, Action<ConfidenceSettings>? configured = null)
    {
        var settings = new ConfidenceSettings();
        configured?.Invoke(settings);
        new LearnedOverridesPostConfigure(new FixedSource(applied), factory.Services.GetServices<IValidateOptions<ConfidenceSettings>>(), NullLogger<LearnedOverridesPostConfigure>.Instance).PostConfigure(Options.DefaultName, settings);
        return settings;
    }

    [Fact]
    public void An_applied_tightening_is_laid_over_the_options_at_start()
    {
        var s = Overlay([new("Confidence:CostStress:TierABps", "4.4", "auto"), new("Confidence:Decay:Sigmas", "0.9", "owner:me@x.test")]);

        s.CostStress.TierABps.ShouldBe(4.4m);
        s.Decay.Sigmas.ShouldBe(0.9);
    }

    [Fact]
    public void An_automatic_value_that_is_not_tighter_than_the_configured_one_is_left_out()
    {
        // the owner configured 4 bps (stricter than the default 3); an automatic 3.5 would loosen it
        Overlay([new("Confidence:CostStress:TierABps", "3.5", "auto")], c => c.CostStress.TierABps = 4m).CostStress.TierABps.ShouldBe(4m);
    }

    [Fact]
    public void A_value_that_would_make_the_options_invalid_is_left_out_so_the_host_still_starts()
    {
        var s = Overlay([new(TierB, "100", "owner:me@x.test"), new("Confidence:CostStress:TierABps", "4.4", "auto")]);

        s.CostStress.TierBCutCrore.ShouldBe(20m);
        s.CostStress.TierABps.ShouldBe(4.4m); // the others still apply
        ConfidenceOverlay.Failures(factory.Services.GetServices<IValidateOptions<ConfidenceSettings>>(), s).ShouldBeEmpty();
    }

    [Fact]
    public void A_key_off_the_allow_list_an_unknown_key_or_an_unreadable_log_changes_nothing()
    {
        var s = Overlay([new("Confidence:Loop:Level", "0.97", "owner:me@x.test"), new("Confidence:Nope:Key", "1", "auto"), new("Confidence:CostStress:TierABps", "banana", "auto")]);

        s.Loop.Level.ShouldBe(new ConfidenceSettings().Loop.Level);
        s.CostStress.TierABps.ShouldBe(3m);
        Overlay(null).CostStress.TierABps.ShouldBe(3m);
        Overlay([]).CostStress.TierABps.ShouldBe(3m);
    }

    [Fact]
    public void The_configuration_binds_the_learned_guardrails_with_their_defaults()
    {
        var options = factory.Services.GetRequiredService<LearnedSettingsOptions>();

        (options.WeeklyCap, options.MaxStepPercent, options.MaxTotalPercent).ShouldBe((3, 10, 30));
        _ = factory.Services.GetRequiredService<IConfiguration>();
    }
}
