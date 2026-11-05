using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Confidence;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-12 part 12a (ADR 0218): <c>Confidence:Decay</c> and <c>Confidence:Loop</c> are bound and validated at start, and configuration can only tighten the spec's proposals: a looser value stops the host. They are configuration sections, not the versioned home of ADR 0002.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ConfidenceSettingsApiTests(ApiFactory factory)
{
    [Fact]
    public void The_defaults_are_the_specs_proposals()
    {
        factory.Services.GetRequiredService<DecayOptions>().ShouldBe(new DecayOptions());
        factory.Services.GetRequiredService<LoopOptions>().ShouldBe(new LoopOptions());
    }

    [Fact]
    public void The_sections_are_bound_and_a_key_left_out_keeps_its_default()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Confidence:Decay:WindowTrades", "40");
            b.UseSetting("Confidence:Decay:Sigmas", "1.25");
            b.UseSetting("Confidence:Loop:MinDays", "30");
            b.UseSetting("Confidence:Loop:HelpsAt", "0.975");
            b.UseSetting("Confidence:Loop:SlopeSe", "1.5");
        });

        host.Services.GetRequiredService<DecayOptions>().ShouldBe(new DecayOptions(WindowTrades: 40, Sigmas: 1.25));
        host.Services.GetRequiredService<LoopOptions>().ShouldBe(new LoopOptions(MinDays: 30, HelpsAt: 0.975, SlopeSe: 1.5));
    }

    [Theory]
    [InlineData("Decay:WindowTrades", "61")]
    [InlineData("Decay:WindowTrades", "19")]
    [InlineData("Decay:Sigmas", "1.6")]
    [InlineData("Decay:Sigmas", "0")]
    [InlineData("Decay:QuarterFloorR", "-0.06")]
    [InlineData("Decay:QuarterFloorR", "0.01")]
    [InlineData("Loop:MinDays", "19")]
    [InlineData("Loop:Draws", "1999")]
    [InlineData("Loop:Level", "0.8")]
    [InlineData("Loop:HelpsAt", "0.94")]
    [InlineData("Loop:HelpsAt", "1")]
    [InlineData("Loop:SlopeLow", "0.7")]
    [InlineData("Loop:SlopeHigh", "1.3")]
    [InlineData("Loop:SlopeSe", "2.5")]
    [InlineData("Loop:MinSlopePer50Rows", "0")]
    public void A_looser_or_out_of_range_value_stops_the_host_at_start(string key, string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:" + key, value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }
}
