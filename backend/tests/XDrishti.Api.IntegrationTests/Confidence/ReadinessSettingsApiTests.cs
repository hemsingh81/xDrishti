using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Domain.Readiness;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-11 part 1 (ADR 0221): <c>Confidence:Readiness</c> is bound and validated at start, and configuration can only tighten the spec's proposals, in both directions of use: a looser value stops the host. The enforce switch is not a configuration value; it is the owner's stored row.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReadinessSettingsApiTests(ApiFactory factory)
{
    [Fact]
    public void The_defaults_are_the_specs_proposals()
    {
        factory.Services.GetRequiredService<ReadinessOptions>().ShouldBe(new ReadinessOptions());
    }

    [Fact]
    public async Task The_section_is_bound_and_a_key_left_out_keeps_its_default_and_the_enforce_switch_is_not_a_configuration_value()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Confidence:Readiness:MinNEff", "400");
            b.UseSetting("Confidence:Readiness:MaxYearShare", "0.5");
            b.UseSetting("Confidence:Readiness:ReadyFrom", "90");
            b.UseSetting("Confidence:Readiness:Enforce", "true"); // there is no such key: it is ignored, the switch is the owner's stored row
        });

        host.Services.GetRequiredService<ReadinessOptions>().ShouldBe(new ReadinessOptions(MinNEff: 400, MaxYearShare: 0.5, ReadyFrom: 90));
        using var scope = host.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IReadinessSettings>().CurrentAsync(TestContext.Current.CancellationToken)).Enforce.ShouldBeFalse();
    }

    [Theory]
    [InlineData("MinNEff", "299")]
    [InlineData("MinTradesPerCell", "29")]
    [InlineData("ExpectancyR", "0.14")]
    [InlineData("TStatistic", "1.9")]
    [InlineData("MinQuarterShare", "0.69")]
    [InlineData("MaxYearShare", "0.61")]
    [InlineData("MaxInstrumentShare", "0.16")]
    [InlineData("DrawdownBudgetR", "21")]
    [InlineData("BuildingFrom", "39")]
    [InlineData("UsableFrom", "69")]
    [InlineData("ReadyFrom", "84")]
    [InlineData("ReadyFrom", "101")]
    [InlineData("MinNEff", "NaN")]
    public void A_looser_or_out_of_range_value_stops_the_host_at_start(string key, string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Readiness:" + key, value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }

    [Fact]
    public void Label_cut_offs_that_do_not_increase_stop_the_host_at_start()
    {
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Confidence:Readiness:UsableFrom", "90");
            b.UseSetting("Confidence:Readiness:ReadyFrom", "90");
        });

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }
}
