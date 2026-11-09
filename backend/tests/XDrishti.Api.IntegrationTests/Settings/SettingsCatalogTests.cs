using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.Confidence;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Settings;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Api.IntegrationTests.Settings;

/// <summary>AU-08 (ADR 0242): the Confidence keys all have descriptors, defaults match, bounds are checked at start, an undeclared direction loosens both ways, and the catalog shows no secret.</summary>
[Collection(ApiTestSuite.Name)]
public sealed partial class SettingsCatalogTests(ApiFactory factory)
{
    private static IEnumerable<string> Leaves(Type type, string prefix)
    {
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var path = prefix.Length == 0 ? p.Name : prefix + ":" + p.Name;
            if (p.PropertyType.IsNested && p.PropertyType.IsClass)
            {
                foreach (var leaf in Leaves(p.PropertyType, path))
                {
                    yield return leaf;
                }
            }
            else
            {
                yield return path;
            }
        }
    }

    [GeneratedRegex("secret|password|passwd|token|credential|apikey|api_key|privatekey|private_key", RegexOptions.IgnoreCase)]
    private static partial Regex SecretLooking();

    [Fact]
    public void Every_key_of_the_confidence_section_has_a_descriptor()
    {
        var described = ConfidenceSettingDescriptors.All.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

        var missing = Leaves(typeof(ConfidenceSettings), ConfidenceSettings.SectionName).Where(k => !described.Contains(k)).ToList();

        missing.ShouldBeEmpty("Confidence keys with no descriptor: " + string.Join(", ", missing));
        described.ShouldBe(Leaves(typeof(ConfidenceSettings), ConfidenceSettings.SectionName).ToHashSet(StringComparer.Ordinal), ignoreOrder: true); // no stray descriptor either
        described.Count.ShouldBe(ConfidenceSettingDescriptors.All.Count);
    }

    [Fact]
    public void A_descriptor_default_is_the_real_default_and_lies_inside_its_range()
    {
        var defaults = new ConfidenceSettings();
        foreach (var d in ConfidenceSettingDescriptors.All.Where(x => x.HasRange && x.Default is not null))
        {
            var value = decimal.Parse(ConfidenceSettingDescriptors.ValueOf(defaults, d.Key[(ConfidenceSettings.SectionName.Length + 1)..])!, System.Globalization.CultureInfo.InvariantCulture);

            d.InRange(value).ShouldBeTrue(d.Key + " default " + value + " is outside " + d.Min + ".." + d.Max);
        }
    }

    [Fact]
    public void The_default_settings_pass_the_range_validator()
    {
        new ConfidenceRangeValidator().Validate(null, new ConfidenceSettings()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_value_outside_a_descriptor_range_fails_the_validator()
    {
        var settings = new ConfidenceSettings();
        settings.Kill.StressMultiplier = 9;

        var result = new ConfidenceRangeValidator().Validate(null, settings);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Confidence:Kill:StressMultiplier");
    }

    [Fact]
    public void A_value_outside_a_range_stops_the_start_up_with_the_key_named()
    {
        var services = new ServiceCollection();
        services.AddOptions<ConfidenceSettings>().Configure(o => o.Kill.StressMultiplier = 9).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ConfidenceSettings>, ConfidenceRangeValidator>(); // alone: only the descriptor bounds reject here
        using var provider = services.BuildServiceProvider();

        var thrown = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ConfidenceSettings>>().Value);

        thrown.Message.ShouldContain("Confidence:Kill:StressMultiplier");
    }

    [Fact]
    public void A_value_outside_a_range_also_stops_the_host()
    {
        Should.Throw<Exception>(() =>
        {
            using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Kill:StressMultiplier", "9"));
            host.CreateClient();
        });
    }

    /// <summary>Every declared direction, pinned: a change here is a safety review. Every other key is None and loosens both ways.</summary>
    private static readonly Dictionary<string, SettingSafer> Declared = new(StringComparer.Ordinal)
    {
        ["Confidence:CostStress:CapacityFillShare"] = SettingSafer.Lower,
        ["Confidence:CostStress:CapacityDailyShare"] = SettingSafer.Lower,
        ["Confidence:CostStress:TierACutCrore"] = SettingSafer.Higher,
        ["Confidence:CostStress:TierBCutCrore"] = SettingSafer.Higher,
        ["Confidence:CostStress:TierABps"] = SettingSafer.Higher,
        ["Confidence:CostStress:TierBBps"] = SettingSafer.Higher,
        ["Confidence:CostStress:RangeFraction"] = SettingSafer.Higher,
        ["Confidence:CostStress:MinTurnoverCrore"] = SettingSafer.Higher,
        ["Confidence:Pbo:MaxPbo"] = SettingSafer.Lower,
        ["Confidence:MlInfluence:HoldOutMinRows"] = SettingSafer.Higher,
        ["Confidence:MlInfluence:HoldOutMinProbability"] = SettingSafer.Higher,
        ["Confidence:MlInfluence:ForwardMinCandidates"] = SettingSafer.Higher,
        ["Confidence:MlInfluence:ForwardMinWeeks"] = SettingSafer.Higher,
        ["Confidence:MlInfluence:ForwardMaxEcePp"] = SettingSafer.Lower,
        ["Confidence:Decay:Sigmas"] = SettingSafer.Lower,
        ["Confidence:Decay:QuarterFloorR"] = SettingSafer.Higher,
        ["Confidence:Loop:Level"] = SettingSafer.Higher,
        ["Confidence:Loop:HelpsAt"] = SettingSafer.Higher,
        ["Confidence:Kill:StressMultiplier"] = SettingSafer.Higher,
        ["Confidence:Kill:DrawdownFactor"] = SettingSafer.Lower,
        ["Confidence:Readiness:MinNEff"] = SettingSafer.Higher,
        ["Confidence:Readiness:MinTradesPerCell"] = SettingSafer.Higher,
        ["Confidence:Readiness:ExpectancyR"] = SettingSafer.Higher,
        ["Confidence:Readiness:TStatistic"] = SettingSafer.Higher,
        ["Confidence:Readiness:MinQuarterShare"] = SettingSafer.Higher,
        ["Confidence:Readiness:MaxYearShare"] = SettingSafer.Lower,
        ["Confidence:Readiness:MaxInstrumentShare"] = SettingSafer.Lower,
        ["Confidence:Readiness:DrawdownBudgetR"] = SettingSafer.Lower,
        ["Confidence:Readiness:BuildingFrom"] = SettingSafer.Higher,
        ["Confidence:Readiness:UsableFrom"] = SettingSafer.Higher,
        ["Confidence:Readiness:ReadyFrom"] = SettingSafer.Higher,
        ["StrategyLab:Gates:MinOosTrades"] = SettingSafer.Higher,
        ["StrategyLab:Gates:MinExpectancyR"] = SettingSafer.Higher,
        ["StrategyLab:Gates:MinProfitFactor"] = SettingSafer.Higher,
        ["StrategyLab:Gates:MinPositiveFoldsPct"] = SettingSafer.Higher,
        ["StrategyLab:Gates:MaxDrawdownR"] = SettingSafer.Lower,
        ["StrategyLab:Gates:MaxCalibrationErrorPp"] = SettingSafer.Lower,
    };

    [Fact]
    public void The_declared_directions_are_exactly_the_pinned_table()
    {
        var actual = ConfidenceSettingDescriptors.All.Concat(SettingsRegistry.All).Where(d => d.Safer != SettingSafer.None).ToDictionary(d => d.Key, d => d.Safer, StringComparer.Ordinal);

        actual.OrderBy(x => x.Key).ShouldBe(Declared.OrderBy(x => x.Key));
    }

    [Theory]
    [InlineData("Confidence:Sequential:Alpha")]
    [InlineData("Confidence:Sequential:BetaCaution")]
    [InlineData("Confidence:Sequential:BetaFail")]
    [InlineData("Confidence:Ladder:MinRows")]
    [InlineData("Confidence:Loop:MinSlopePer50Rows")]
    public void The_keys_that_weaken_a_detector_in_either_direction_loosen_both_ways(string key)
    {
        var d = ConfidenceSettingDescriptors.All.Single(x => x.Key == key);

        d.Safer.ShouldBe(SettingSafer.None);
        d.IsLoosening(0.05m, 0.04m).ShouldBeTrue();
        d.IsLoosening(0.05m, 0.06m).ShouldBeTrue();
        d.IsLoosening(0.05m, 0.05m).ShouldBeFalse();
    }

    [Fact]
    public async Task The_catalog_needs_a_session_and_answers_get_only()
    {
        using var anonymous = factory.CreateAnonymousClient();
        using var denied = await anonymous.GetAsync(new Uri("/api/settings/catalog", UriKind.Relative), TestContext.Current.CancellationToken);
        denied.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);

        var signedIn = await factory.SignInAsync(TestContext.Current.CancellationToken);
        using var post = await signedIn.Client.PostAsJsonAsync(new Uri("/api/settings/catalog", UriKind.Relative), new { }, TestContext.Current.CancellationToken);
        using var put = await signedIn.Client.PutAsJsonAsync(new Uri("/api/settings/catalog", UriKind.Relative), new { }, TestContext.Current.CancellationToken);
        using var delete = await signedIn.Client.DeleteAsync(new Uri("/api/settings/catalog", UriKind.Relative), TestContext.Current.CancellationToken);

        post.StatusCode.ShouldBe(System.Net.HttpStatusCode.MethodNotAllowed);
        put.StatusCode.ShouldBe(System.Net.HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.ShouldBe(System.Net.HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData(SettingSafer.None, 5, 6, true)]
    [InlineData(SettingSafer.None, 5, 4, true)]
    [InlineData(SettingSafer.None, 5, 5, false)]
    [InlineData(SettingSafer.Higher, 5, 4, true)]
    [InlineData(SettingSafer.Higher, 5, 6, false)]
    [InlineData(SettingSafer.Lower, 5, 6, true)]
    [InlineData(SettingSafer.Lower, 5, 4, false)]
    public void A_key_with_no_declared_direction_loosens_both_ways(SettingSafer safer, int from, int to, bool loosens)
    {
        var d = new SettingDescriptor("x", "x", SettingTier.Internal, SettingKind.Configuration, null, null, null, safer, false);

        d.IsLoosening(from, to).ShouldBe(loosens);
    }

    [Fact]
    public void The_essential_tier_is_the_five_and_never_more_than_eight()
    {
        var all = ConfidenceSettingDescriptors.All.Concat(SettingsRegistry.All).ToList();

        all.Count(d => d.Tier == SettingTier.Essential).ShouldBe(5);
        all.Count(d => d.Tier == SettingTier.Essential).ShouldBeLessThanOrEqualTo(SettingsRegistry.MaxEssential);
        all.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count().ShouldBe(all.Count);
    }

    [Fact]
    public void No_descriptor_key_looks_like_a_secret_and_only_the_allow_list_is_learnable()
    {
        var all = ConfidenceSettingDescriptors.All.Concat(SettingsRegistry.All).ToList();

        all.Where(d => SecretLooking().IsMatch(d.Key)).ShouldBeEmpty();
        all.Where(d => d.Learnable).Select(d => d.Key).ShouldBe(XDrishti.Application.LearnedSettings.LearnedAllowList.Keys, ignoreOrder: true);
    }

    [Fact]
    public async Task The_catalog_endpoint_returns_the_descriptors_without_a_secret()
    {
        var signedIn = await factory.SignInAsync(TestContext.Current.CancellationToken);

        var response = await signedIn.Client.GetFromJsonAsync<SettingsCatalogResponse>("/api/settings/catalog", TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        response.Items.Count(i => i.Tier == "Essential").ShouldBe(5);
        response.Items.ShouldContain(i => i.Key == "Confidence:Kill:StressMultiplier" && i.Safer == "higher" && i.Source == "default" && i.Range!.Max == 3);
        response.Items.Where(i => SecretLooking().IsMatch(i.Key)).ShouldBeEmpty();
        response.Items.Where(i => i.Source is "stored" or "section").ShouldAllBe(i => i.Value == null);
    }
}
