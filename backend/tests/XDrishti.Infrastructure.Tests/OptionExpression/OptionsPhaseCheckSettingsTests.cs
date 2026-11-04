using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.OptionPhaseCheck;

namespace XDrishti.Infrastructure.Tests.OptionExpression;

/// <summary>P14-17 (settings): <c>Options:PhaseCheck:StaleAfterDays</c> is 90 by default and from 7 to 730; a missing value is refused, not read as a number.</summary>
public sealed class OptionsPhaseCheckSettingsTests
{
    private static List<ValidationResult> Validate(OptionsPhaseCheckOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void The_default_is_ninety_days()
    {
        var options = new OptionsPhaseCheckOptions();

        options.StaleAfterDays.ShouldBe(90);
        Validate(options).ShouldBeEmpty();
        new OptionsPhaseCheckSettings(Options.Create(options)).StaleAfterDays.ShouldBe(90);
        OptionsPhaseCheckOptions.SectionName.ShouldBe("Options:PhaseCheck");
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(730, true)]
    [InlineData(731, false)]
    public void The_bound_is_from_7_to_730_days(int value, bool valid) => Validate(new OptionsPhaseCheckOptions { StaleAfterDays = value }).Count.ShouldBe(valid ? 0 : 1);

    [Fact]
    public void A_setting_left_null_is_refused() => Validate(new OptionsPhaseCheckOptions { StaleAfterDays = null }).ShouldNotBeEmpty();
}
