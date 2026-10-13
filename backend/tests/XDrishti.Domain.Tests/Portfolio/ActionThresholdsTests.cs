using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The thresholds of the Action-center rules (ADR 0022): the documented defaults and the fixed ranges.</summary>
public sealed class ActionThresholdsTests
{
    private static string[] Problems(ActionThresholds thresholds)
    {
        var errors = new FieldErrors();
        thresholds.Validate(errors);
        return [.. errors.Items.Select(e => e.Field)];
    }

    [Fact]
    public void The_defaults_are_the_ones_of_the_design_and_are_valid()
    {
        ActionThresholds.Defaults.ShouldBe(new ActionThresholds(10m, 20m, 25m, 15m, 1.5m, 3, 35m, 10m, 180, 30));
        Problems(ActionThresholds.Defaults).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("BelowCostPct", 0.99, "belowCostPct")]
    [InlineData("BelowCostPct", 90.01, "belowCostPct")]
    [InlineData("BelowCostHighPct", 95.01, "belowCostHighPct")]
    [InlineData("GainPct", 0.5, "gainPct")]
    [InlineData("GainPct", 1000.01, "gainPct")]
    [InlineData("OverweightFloorPct", 0, "overweightFloorPct")]
    [InlineData("OverweightMultiple", 10.5, "overweightMultiple")]
    [InlineData("MinPositions", 1, "minPositions")]
    [InlineData("MinPositions", 51, "minPositions")]
    [InlineData("ConcentrationPct", 4.99, "concentrationPct")]
    [InlineData("LagPp", 100.5, "lagPp")]
    [InlineData("LagDays", 29, "lagDays")]
    [InlineData("LagDays", 3651, "lagDays")]
    [InlineData("LongTermWindowDays", 366, "longTermWindowDays")]
    public void A_value_outside_its_range_is_a_problem_for_that_field(string property, double value, string field)
    {
        var thresholds = property switch
        {
            "BelowCostPct" => ActionThresholds.Defaults with { BelowCostPct = (decimal)value },
            "BelowCostHighPct" => ActionThresholds.Defaults with { BelowCostHighPct = (decimal)value },
            "GainPct" => ActionThresholds.Defaults with { GainPct = (decimal)value },
            "OverweightFloorPct" => ActionThresholds.Defaults with { OverweightFloorPct = (decimal)value },
            "OverweightMultiple" => ActionThresholds.Defaults with { OverweightMultiple = (decimal)value },
            "MinPositions" => ActionThresholds.Defaults with { MinPositions = (int)value },
            "ConcentrationPct" => ActionThresholds.Defaults with { ConcentrationPct = (decimal)value },
            "LagPp" => ActionThresholds.Defaults with { LagPp = (decimal)value },
            "LagDays" => ActionThresholds.Defaults with { LagDays = (int)value },
            _ => ActionThresholds.Defaults with { LongTermWindowDays = (int)value },
        };

        Problems(thresholds).ShouldContain(field);
    }

    [Fact]
    public void The_high_band_must_be_above_the_below_cost_band()
    {
        Problems(ActionThresholds.Defaults with { BelowCostPct = 30m, BelowCostHighPct = 30m }).ShouldBe(["belowCostHighPct"]);
        Problems(ActionThresholds.Defaults with { BelowCostPct = 30m, BelowCostHighPct = 30.01m }).ShouldBeEmpty();
    }

    [Fact]
    public void More_than_two_decimals_is_a_problem()
    {
        Problems(ActionThresholds.Defaults with { LagPp = 10.005m }).ShouldBe(["lagPp"]);
    }
}
