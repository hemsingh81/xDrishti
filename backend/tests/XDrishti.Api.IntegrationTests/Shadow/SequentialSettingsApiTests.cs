using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Confidence;

namespace XDrishti.Api.IntegrationTests.Shadow;

/// <summary>CF-09 (ADR 0213): <c>Confidence:Sequential</c> is bound and validated at start. The spec's numbers by default, a key left out keeps its default, and a value out of range stops the host.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class SequentialSettingsApiTests(ApiFactory factory)
{
    [Fact]
    public void The_defaults_are_the_specs_numbers()
    {
        factory.Services.GetRequiredService<SequentialOptions>().ShouldBe(new SequentialOptions());
    }

    [Fact]
    public void The_sequential_section_is_bound_and_a_key_left_out_keeps_its_default()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Sequential:Mu1", "0.25").UseSetting("Confidence:Sequential:SigmaFromN", "30").UseSetting("Confidence:Sequential:BetaFail", "0.04"));

        host.Services.GetRequiredService<SequentialOptions>().ShouldBe(new SequentialOptions(Mu1: 0.25, BetaFail: 0.04, SigmaFromN: 30));
    }

    [Theory]
    [InlineData("Confidence:Sequential:Mu1", "0")]
    [InlineData("Confidence:Sequential:Mu1", "3")]
    [InlineData("Confidence:Sequential:Sigma", "0.1")]
    [InlineData("Confidence:Sequential:Alpha", "0")]
    [InlineData("Confidence:Sequential:BetaCaution", "0.6")]
    [InlineData("Confidence:Sequential:BetaFail", "0.2")]
    [InlineData("Confidence:Sequential:SigmaFromN", "4")]
    public void A_value_out_of_range_stops_the_host_at_start(string key, string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting(key, value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }
}
