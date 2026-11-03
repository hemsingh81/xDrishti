using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.OptionExpression;

namespace XDrishti.Infrastructure.Tests.OptionExpression;

/// <summary>P14-08 (settings): <c>Options:Remark:MaxIvAgeMinutes</c> is 60 by default and from 5 to 390; a missing value is refused, not read as a number.</summary>
public sealed class RemarkSettingsTests
{
    private static List<ValidationResult> Validate(RemarkOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void The_default_is_sixty_minutes()
    {
        var options = new RemarkOptions();

        options.MaxIvAgeMinutes.ShouldBe(60);
        Validate(options).ShouldBeEmpty();
        new RemarkSettings(Options.Create(options)).MaxIvAgeMinutes.ShouldBe(60);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(390, true)]
    [InlineData(391, false)]
    public void The_oldest_iv_is_from_5_to_390_minutes(int value, bool valid) => Validate(new RemarkOptions { MaxIvAgeMinutes = value }).Count.ShouldBe(valid ? 0 : 1);

    [Fact]
    public void A_setting_left_null_is_refused() => Validate(new RemarkOptions { MaxIvAgeMinutes = null }).ShouldNotBeEmpty();
}
