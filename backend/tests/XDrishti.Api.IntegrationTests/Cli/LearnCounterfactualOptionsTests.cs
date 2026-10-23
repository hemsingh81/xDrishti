using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn counterfactual</c> (spec P6-12): pure parsing, no database.</summary>
public sealed class LearnCounterfactualOptionsTests
{
    private static readonly string Key = new('a', 64);

    [Fact]
    public void Defaults_apply_for_what_is_left_out()
    {
        var (options, errors) = LearnCounterfactualOptions.Parse(["--run", $"{Key}:intraday:FIXED_RR"]);

        errors.ShouldBeEmpty();
        options!.Sources.Count.ShouldBe(1);
        options.Counterfactual.FrozenMonths.ShouldBe(12);
        options.Counterfactual.RetrainMonths.ShouldBe(3);
        options.HoldOutMonths.ShouldBe(6);
    }

    [Fact]
    public void The_knobs_can_be_set()
    {
        var (options, _) = LearnCounterfactualOptions.Parse(["--run", $"{Key}:swing:X", "--frozen-months", "6", "--retrain-months", "2", "--min-gain", "0.01", "--trials", "4", "--hold-out-months", "0"]);

        options!.Counterfactual.FrozenMonths.ShouldBe(6);
        options.Counterfactual.RetrainMonths.ShouldBe(2);
        options.Counterfactual.MinGainLogLoss.ShouldBe(0.01);
        options.Counterfactual.Trials.ShouldBe(4);
        options.HoldOutMonths.ShouldBe(0);
    }

    [Theory]
    [InlineData("--frozen-months", "x")]
    [InlineData("--retrain-months", "1.5")]
    [InlineData("--min-gain", "NaN")]
    [InlineData("--trials", "x")]
    [InlineData("--hold-out-months", "x")]
    [InlineData("--bogus", "1")]
    public void Bad_values_are_listed(string flag, string value)
    {
        var (options, errors) = LearnCounterfactualOptions.Parse(["--run", $"{Key}:intraday:X", flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_run_a_bad_run_a_missing_value_and_a_repeat_are_refused()
    {
        LearnCounterfactualOptions.Parse([]).Errors.ShouldContain("give at least one --run");
        LearnCounterfactualOptions.Parse(["--run", "abc"]).Errors.ShouldContain("--run is RUNKEY:STYLE:EXIT");
        LearnCounterfactualOptions.Parse(["--run", $"{Key}:a:b", "--trials"]).Errors.ShouldContain("'--trials' needs a value");
        LearnCounterfactualOptions.Parse(["--run", $"{Key}:a:b", "--trials", "1", "--trials", "2"]).Errors.ShouldContain("'--trials' is given twice");
    }
}
