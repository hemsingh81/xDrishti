using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Confidence;

namespace XDrishti.Api.IntegrationTests.Shadow;

/// <summary>CF-10 part 10b (ADR 0215): <c>Confidence:Ladder</c> is bound and validated at start.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LadderSettingsApiTests(ApiFactory factory)
{
    [Fact]
    public void The_default_is_report_only_with_the_specs_proposals()
    {
        factory.Services.GetRequiredService<LadderOptions>().ShouldBe(new LadderOptions());
    }

    [Fact]
    public void The_section_is_bound()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Confidence:Ladder:Mode", "Off");
            b.UseSetting("Confidence:Ladder:Epv", "10");
            b.UseSetting("Confidence:Ladder:MaxLogisticFeatures", "6");
            b.UseSetting("Confidence:Ladder:MinTreeEvents", "150");
            b.UseSetting("Confidence:Ladder:MinProbability", "0.9");
            b.UseSetting("Confidence:Ladder:PsiWatch", "0.2");
            b.UseSetting("Confidence:Ladder:PsiAlarm", "0.4");
            b.UseSetting("Confidence:Ladder:PsiBins", "5");
            b.UseSetting("Confidence:Ladder:MinRows", "40");
            b.UseSetting("Confidence:Ladder:MaxFlipShare", "0.25");
        });

        host.Services.GetRequiredService<LadderOptions>().ShouldBe(new LadderOptions(ConfidenceMode.Off, 10, 6, 150, 0.9, 0.2, 0.4, 5, 40, 0.25));
    }

    [Theory]
    [InlineData("Mode", "Gate")]
    [InlineData("Epv", "4")]
    [InlineData("MaxLogisticFeatures", "0")]
    [InlineData("MinTreeEvents", "5")]
    [InlineData("MinProbability", "1")]
    [InlineData("PsiAlarm", "0.05")]
    [InlineData("PsiBins", "1")]
    [InlineData("MinRows", "1")]
    [InlineData("MaxFlipShare", "0.5")]
    public void A_value_out_of_range_stops_the_host_at_start(string key, string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Ladder:" + key, value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }
}
