using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Confidence;

namespace XDrishti.Api.IntegrationTests.Shadow;

/// <summary>CF-10 part 10c-1 (ADR 0216): <c>Confidence:PairedBootstrap</c> is bound and validated at start.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PairedBootstrapSettingsApiTests(ApiFactory factory)
{
    [Fact]
    public void The_default_is_report_only_with_the_specs_proposals()
    {
        factory.Services.GetRequiredService<PairedBootstrapOptions>().ShouldBe(new PairedBootstrapOptions());
    }

    [Fact]
    public void The_section_is_bound()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Confidence:PairedBootstrap:Mode", "Off");
            b.UseSetting("Confidence:PairedBootstrap:Draws", "3000");
            b.UseSetting("Confidence:PairedBootstrap:Level", "0.8");
            b.UseSetting("Confidence:PairedBootstrap:MinDays", "20");
            b.UseSetting("Confidence:PairedBootstrap:PromisingFrom", "0.85");
        });

        host.Services.GetRequiredService<PairedBootstrapOptions>().ShouldBe(new PairedBootstrapOptions(ConfidenceMode.Off, 3000, 0.8, 20, 0.85));
    }

    [Theory]
    [InlineData("Mode", "Gate")]
    [InlineData("Draws", "99")]
    [InlineData("Level", "1")]
    [InlineData("MinDays", "1")]
    [InlineData("PromisingFrom", "0.96")] // above the ladder's pass bar of 0.95
    [InlineData("PromisingFrom", "0.4")]
    public void A_value_out_of_range_stops_the_host_at_start(string key, string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:PairedBootstrap:" + key, value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }
}
