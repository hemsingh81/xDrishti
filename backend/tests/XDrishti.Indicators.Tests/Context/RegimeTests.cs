using XDrishti.Domain.Common;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>Criterion 11: the nine labels are the spellings of doc 07 §6, <c>Name</c> and <c>Code</c> are inverses over 1 to 9, and an unknown name is an error (never an exception).</summary>
public sealed class RegimeTests
{
    private static readonly string[] Spellings =
    [
        "UP_LOW_VOL", "UP_NORMAL_VOL", "UP_HIGH_VOL",
        "RANGE_LOW_VOL", "RANGE_NORMAL_VOL", "RANGE_HIGH_VOL",
        "DOWN_LOW_VOL", "DOWN_NORMAL_VOL", "DOWN_HIGH_VOL",
    ];

    public static TheoryData<int> Codes { get; } = [.. Enumerable.Range(1, 9)];

    [Theory]
    [MemberData(nameof(Codes))]
    public void Name_gives_the_spelling_of_the_code_and_Code_gives_the_code_back(int code)
    {
        var name = Regime.Name(code);

        name.ShouldBe(Spellings[code - 1]);
        var back = Regime.Code(name);
        back.IsSuccess.ShouldBeTrue();
        back.Value.ShouldBe(code);
    }

    [Fact]
    public void The_nine_names_are_distinct_and_run_from_up_low_to_down_high()
    {
        var names = Enumerable.Range(1, 9).Select(Regime.Name).ToList();

        names.Distinct().Count().ShouldBe(9);
        names[0].ShouldBe("UP_LOW_VOL");
        names[8].ShouldBe("DOWN_HIGH_VOL");
    }

    [Fact]
    public void The_code_is_three_times_the_trend_band_plus_the_volatility_level_plus_one()
    {
        // trend +1 / 0 / -1 and volatility 0 / 1 / 2 as the context carries them
        var trends = new[] { (1, "UP"), (0, "RANGE"), (-1, "DOWN") };
        var levels = new[] { (0, "LOW"), (1, "NORMAL"), (2, "HIGH") };

        foreach (var (trend, trendName) in trends)
        {
            foreach (var (level, levelName) in levels)
            {
                var code = (1 - trend) * 3 + level + 1;
                Regime.Name(code).ShouldBe($"{trendName}_{levelName}_VOL");
            }
        }
    }

    [Theory]
    [InlineData("up_low_vol")]
    [InlineData("Up_Low_Vol")]
    [InlineData(" UP_LOW_VOL")]
    [InlineData("UP_LOW_VOL ")]
    [InlineData("UP_LOW")]
    [InlineData("UP-LOW-VOL")]
    [InlineData("SIDEWAYS_LOW_VOL")]
    [InlineData("1")]
    [InlineData("")]
    public void An_unknown_or_differently_spelled_name_is_a_validation_error_not_an_exception(string name)
    {
        var result = Regime.Code(name);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Regime.UnknownNameCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Message.ShouldContain("UP_LOW_VOL");
    }

    [Fact]
    public void A_null_name_is_an_error_too()
    {
        var result = Regime.Code(null!);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Regime.UnknownNameCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Name_of_a_code_outside_one_to_nine_is_a_programming_error(int code)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Regime.Name(code)).ParamName.ShouldBe("code");
    }
}
