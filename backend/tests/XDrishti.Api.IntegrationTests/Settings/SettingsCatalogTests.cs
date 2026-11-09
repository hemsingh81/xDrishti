using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
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
    public void A_value_outside_a_range_stops_the_host_at_start()
    {
        Should.Throw<Exception>(() =>
        {
            using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Kill:StressMultiplier", "9"));
            host.CreateClient();
        });
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
    public void No_descriptor_key_looks_like_a_secret_and_learning_is_off_everywhere()
    {
        var all = ConfidenceSettingDescriptors.All.Concat(SettingsRegistry.All).ToList();

        all.Where(d => SecretLooking().IsMatch(d.Key)).ShouldBeEmpty();
        all.ShouldAllBe(d => !d.Learnable);
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
