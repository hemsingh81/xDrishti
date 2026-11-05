using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Confidence;

namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>CF-07 part C (ADR 0210): <c>Confidence:Plateau</c> is bound and validated at start. Report-only by default, a value out of range stops the host, and Enforce needs the block it lives in to be computed.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlateauSettingsApiTests(ApiFactory factory)
{
    [Fact]
    public void The_defaults_are_report_only_with_enforcement_off()
    {
        factory.Services.GetRequiredService<PlateauOptions>().ShouldBe(new PlateauOptions());
    }

    [Fact]
    public void The_plateau_section_is_bound()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Plateau:Retention", "0.8").UseSetting("Confidence:Plateau:MinPoints", "4").UseSetting("Confidence:Plateau:Enforce", "true"));

        host.Services.GetRequiredService<PlateauOptions>().ShouldBe(new PlateauOptions(ConfidenceMode.ReportOnly, true, 0.8, 4));
    }

    [Theory]
    [InlineData("Confidence:Plateau:Retention", "0.4")]
    [InlineData("Confidence:Plateau:Retention", "0.99")]
    [InlineData("Confidence:Plateau:MinPoints", "2")]
    [InlineData("Confidence:Plateau:MinPoints", "6")]
    [InlineData("Confidence:Plateau:Mode", "Gate")]
    public void A_value_out_of_range_or_the_Gate_mode_stops_the_host_at_start(string key, string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting(key, value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }

    [Fact]
    public void Enforce_with_the_plateau_mode_off_stops_the_host_at_start()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Plateau:Mode", "Off").UseSetting("Confidence:Plateau:Enforce", "true"));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }

    [Fact]
    public void Enforce_while_the_nulls_are_off_stops_the_host_at_start_because_the_gate_lives_in_the_nulls_block()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Nulls:Mode", "Off").UseSetting("Confidence:Plateau:Enforce", "true"));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }

    [Fact]
    public void The_report_only_gate_with_the_nulls_off_starts()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Nulls:Mode", "Off"));

        host.CreateClient().ShouldNotBeNull();
    }
}
