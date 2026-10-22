using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command lines of <c>xd learn features</c> and <c>xd learn training-set</c> (spec P6-04): pure parsing, no database.</summary>
public sealed class LearnFeaturesOptionsTests
{
    private static readonly string Key = new('a', 64);

    [Fact]
    public void Features_takes_one_or_more_runs()
    {
        var (options, errors) = LearnFeaturesOptions.Parse(["--run", Key, "--run", new string('b', 64)]);

        errors.ShouldBeEmpty();
        options!.RunKeys.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData]
    [InlineData("--run")]
    [InlineData("--runs", "x")]
    public void Features_refuses_a_missing_run_a_missing_value_and_an_unknown_argument(params string[] args)
    {
        var (options, errors) = LearnFeaturesOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_training_set_line_takes_runs_a_required_list_and_the_cell_knobs()
    {
        var (options, errors) = LearnTrainingSetOptions.Parse(["--run", $"{Key}:intraday:FIXED_RR", "--require", "rsi14, adx14", "--min-trades", "10"]);

        errors.ShouldBeEmpty();
        options!.Sources.Single().Exit.ShouldBe("FIXED_RR");
        options.Required.ShouldBe(["rsi14", "adx14"]);
        options.Options.MinTrades.ShouldBe(10);
        options.Options.PriorStrength.ShouldBe(20);
    }

    [Fact]
    public void Without_a_require_list_the_catalog_default_applies()
    {
        var (options, _) = LearnTrainingSetOptions.Parse(["--run", $"{Key}:swing:ATR"]);

        options!.Required.ShouldBeNull();
    }

    [Theory]
    [InlineData("--run", "k:swing")]
    [InlineData("--require", "a")]
    [InlineData("--run", "a:b:c", "--min-trades", "many")]
    [InlineData("--run", "a:b:c", "--require", "x", "--require", "y")]
    [InlineData("--bogus", "1")]
    public void The_training_set_line_refuses_what_it_cannot_read(params string[] args)
    {
        var (options, errors) = LearnTrainingSetOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }
}
