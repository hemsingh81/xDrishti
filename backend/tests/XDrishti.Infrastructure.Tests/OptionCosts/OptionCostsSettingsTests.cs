using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.OptionCosts;

namespace XDrishti.Infrastructure.Tests.OptionCosts;

/// <summary>P14-07 criteria 4 and 9 (settings): the bounds of <c>Options:Costs:*</c> and <c>Options:Liquidity:*</c>, the empty default of the owner's assumed half-spread and its lookup by short symbol.</summary>
public sealed class OptionCostsSettingsTests
{
    private static List<ValidationResult> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void The_defaults_are_the_spec_values_and_the_assumed_half_spread_is_empty()
    {
        var costs = new OptionCostsOptions();
        var liquidity = new OptionLiquidityOptions();

        costs.MinUsableSessions.ShouldBe(60);
        costs.MinQuotesPerBucket.ShouldBe(200);
        costs.AssumedHalfSpreadPct.ShouldBeEmpty();
        liquidity.MinOiNotionalCr.ShouldBe(5m);
        liquidity.MinBarTurnoverLakh.ShouldBe(25m);
        Validate(costs).ShouldBeEmpty();
        Validate(liquidity).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(250, true)]
    [InlineData(251, false)]
    public void The_least_usable_sessions_is_from_20_to_250(int value, bool valid) =>
        Validate(new OptionCostsOptions { MinUsableSessions = value }).Count.ShouldBe(valid ? 0 : 1);

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public void The_least_quotes_per_bucket_is_from_50_to_5000(int value, bool valid) =>
        Validate(new OptionCostsOptions { MinQuotesPerBucket = value }).Count.ShouldBe(valid ? 0 : 1);

    [Fact]
    public void A_setting_left_null_is_refused_not_read_as_a_number()
    {
        Validate(new OptionCostsOptions { MinUsableSessions = null }).ShouldNotBeEmpty();
        Validate(new OptionCostsOptions { MinQuotesPerBucket = null }).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("0.009", false)]
    [InlineData("0.01", true)]
    [InlineData("0.5", true)]
    [InlineData("20", true)]
    [InlineData("20.01", false)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    public void An_assumed_half_spread_is_from_0_01_to_20_percent(string value, bool valid) =>
        Validate(new OptionCostsOptions { AssumedHalfSpreadPct = { ["NIFTY"] = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture) } }).Count.ShouldBe(valid ? 0 : 1);

    [Theory]
    [InlineData("NIFTY", true)]
    [InlineData("M&M", true)]
    [InlineData("BAJAJ-AUTO", true)]
    [InlineData("NSE:NIFTY", false)]
    [InlineData("", false)]
    [InlineData("A B", false)]
    public void An_assumed_half_spread_is_keyed_by_the_short_symbol(string key, bool valid) =>
        Validate(new OptionCostsOptions { AssumedHalfSpreadPct = { [key] = 0.5m } }).Count.ShouldBe(valid ? 0 : 1);

    [Theory]
    [InlineData(-0.1, false)]
    [InlineData(0, true)]
    [InlineData(1000, true)]
    [InlineData(1000.5, false)]
    public void The_oi_threshold_is_from_0_to_1000_crore(double value, bool valid) =>
        Validate(new OptionLiquidityOptions { MinOiNotionalCr = (decimal)value }).Count.ShouldBe(valid ? 0 : 1);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void The_bar_turnover_threshold_is_from_0_to_10000_lakh(int value, bool valid) =>
        Validate(new OptionLiquidityOptions { MinBarTurnoverLakh = value }).Count.ShouldBe(valid ? 0 : 1);

    [Fact]
    public void The_assumption_is_found_by_the_short_symbol_of_the_canonical_symbol_in_any_case_and_an_unset_one_is_null()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Options:Costs:AssumedHalfSpreadPct:NIFTY"] = "0.5",
            ["Options:Costs:AssumedHalfSpreadPct:m&m"] = "1.25",
        }).Build();
        var costs = new OptionCostsOptions();
        configuration.GetSection(OptionCostsOptions.SectionName).Bind(costs);
        var settings = new SpreadModelSettings(Options.Create(costs), Options.Create(new OptionLiquidityOptions { MinOiNotionalCr = 7, MinBarTurnoverLakh = 30 }));

        settings.AssumedHalfSpreadPct("NSE:NIFTY").ShouldBe(0.5m);
        settings.AssumedHalfSpreadPct("NSE:M&M").ShouldBe(1.25m);
        settings.AssumedHalfSpreadPct("NSE:BANKNIFTY").ShouldBeNull();
        settings.AssumedHalfSpreadPct("NIFTY").ShouldBe(0.5m);
        settings.MinUsableSessions.ShouldBe(60);
        settings.MinQuotesPerBucket.ShouldBe(200);
        settings.Liquidity.MinOiNotionalCr.ShouldBe(7m);
        settings.Liquidity.MinBarTurnoverLakh.ShouldBe(30m);
    }

    [Fact]
    public void With_nothing_configured_no_underlying_has_an_assumption()
    {
        var settings = new SpreadModelSettings(Options.Create(new OptionCostsOptions()), Options.Create(new OptionLiquidityOptions()));

        settings.AssumedHalfSpreadPct("NSE:NIFTY").ShouldBeNull();
    }
}
