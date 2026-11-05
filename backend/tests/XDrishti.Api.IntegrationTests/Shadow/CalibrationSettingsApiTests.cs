using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Confidence;

namespace XDrishti.Api.IntegrationTests.Shadow;

/// <summary>CF-10 part 10a (ADR 0214): <c>Confidence:Calibration</c> is bound and validated at start, and the trainer takes its cut from it.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class CalibrationSettingsApiTests(ApiFactory factory)
{
    [Fact]
    public void The_default_is_the_specs_cut_of_500_rows_and_the_trainer_says_so()
    {
        factory.Services.GetRequiredService<CalibrationOptions>().ShouldBe(new CalibrationOptions());
        factory.Services.GetRequiredService<IModelTrainer>().Parameters.ShouldContain("isotonic_min_rows=500");
    }

    [Fact]
    public void The_section_is_bound_and_reaches_the_trainer()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Calibration:IsotonicMinRows", "800"));

        host.Services.GetRequiredService<CalibrationOptions>().ShouldBe(new CalibrationOptions(800));
        host.Services.GetRequiredService<IModelTrainer>().Parameters.ShouldContain("isotonic_min_rows=800");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100001")]
    public void A_value_out_of_range_stops_the_host_at_start(string value)
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Calibration:IsotonicMinRows", value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }
}
