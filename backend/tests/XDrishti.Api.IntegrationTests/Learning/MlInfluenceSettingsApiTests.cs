using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Confidence;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>CF-10 part 10c-2 (ADR 0217): <c>Confidence:MlInfluence</c> is bound and validated at start; configuration can only tighten the spec's proposals. There is no switch in the section: nothing in configuration can turn model influence on.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class MlInfluenceSettingsApiTests(ApiFactory factory)
{
    [Fact]
    public void The_default_is_the_specs_proposals()
    {
        factory.Services.GetRequiredService<MlInfluenceOptions>().ShouldBe(new MlInfluenceOptions());
    }

    [Fact]
    public void The_section_is_bound()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Confidence:MlInfluence:HoldOutMinRows", "250");
            b.UseSetting("Confidence:MlInfluence:HoldOutMinProbability", "0.975");
            b.UseSetting("Confidence:MlInfluence:ForwardMinCandidates", "60");
            b.UseSetting("Confidence:MlInfluence:ForwardMinWeeks", "8");
            b.UseSetting("Confidence:MlInfluence:ForwardMaxEcePp", "3");
        });

        host.Services.GetRequiredService<MlInfluenceOptions>().ShouldBe(new MlInfluenceOptions(250, 0.975, 60, 8, 3));
    }

    [Theory]
    [InlineData("HoldOutMinRows", "99")]
    [InlineData("HoldOutMinProbability", "0.94")]
    [InlineData("HoldOutMinProbability", "1")]
    [InlineData("ForwardMinCandidates", "29")]
    [InlineData("ForwardMinWeeks", "3")]
    [InlineData("ForwardMaxEcePp", "0")]
    [InlineData("ForwardMaxEcePp", "6")]
    public void A_value_out_of_range_stops_the_host_at_start(string key, string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:MlInfluence:" + key, value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }

    [Fact]
    public void The_hold_out_bar_may_not_be_lower_than_the_ladders_pass_bar()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Confidence:Ladder:MinProbability", "0.97");
            b.UseSetting("Confidence:MlInfluence:HoldOutMinProbability", "0.96");
        });

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }

    [Fact]
    public void The_hold_out_bar_at_or_above_the_ladders_pass_bar_is_accepted()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Confidence:Ladder:MinProbability", "0.97");
            b.UseSetting("Confidence:MlInfluence:HoldOutMinProbability", "0.97");
        });

        host.Services.GetRequiredService<MlInfluenceOptions>().HoldOutMinProbability.ShouldBe(0.97);
    }
}
