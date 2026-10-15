using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Tests.Portfolio;

/// <summary>The settings of the self-healing syncs (<c>Portfolio:Heal</c>, ADR 0027): the defaults of the story, how the waits are read, and what the validation refuses.</summary>
public sealed class HealSettingsTests
{
    private static HealSettings Settings(HealOptions options) => new(Options.Create(options));

    private static List<ValidationResult> Validate(HealOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void The_defaults_are_one_five_and_fifteen_minutes_two_failed_days_and_eight_years()
    {
        var settings = Settings(new HealOptions());

        settings.RetryBackoff.ShouldBe([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)]);
        (settings.PersistDays, settings.DeepenMaxYears).ShouldBe((2, 8));
        Validate(new HealOptions()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2", new[] { 2 })]
    [InlineData(" 1 , 10 ,30 ", new[] { 1, 10, 30 })]
    [InlineData("", new int[0])]
    [InlineData("   ", new int[0])]
    public void The_waits_are_read_as_minutes_and_a_blank_switches_the_retries_off(string text, int[] minutes)
    {
        HealSettings.Parse(text).ShouldBe([.. minutes.Select(m => TimeSpan.FromMinutes(m))]);
        Validate(new HealOptions { RetryBackoffMinutes = text }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("1;5")]
    [InlineData("a,b")]
    [InlineData("1,,5")]
    [InlineData("-1")]
    [InlineData("1,2,3,4,5,6,7,8,9,10,11")]
    [InlineData("1000")]
    public void Waits_that_are_not_minutes_separated_by_commas_are_refused(string text)
    {
        Validate(new HealOptions { RetryBackoffMinutes = text }).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(0, 8, false)]
    [InlineData(31, 8, false)]
    [InlineData(2, -1, false)]
    [InlineData(2, 31, false)]
    [InlineData(1, 0, true)]
    [InlineData(30, 30, true)]
    public void The_days_and_the_years_have_bounds(int persistDays, int deepenYears, bool valid)
    {
        Validate(new HealOptions { PersistDays = persistDays, DeepenMaxYears = deepenYears }).Count.ShouldBe(valid ? 0 : 1);
    }
}
