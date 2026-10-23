using XDrishti.Api.Cli;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command lines of <c>xd learn model</c> and <c>xd learn designate</c> (spec P6-07): pure parsing, no database.</summary>
public sealed class LearnModelOptionsTests
{
    private static readonly string Key = new('a', 64);

    [Fact]
    public void Model_defaults_keep_a_six_month_hold_out()
    {
        var (options, errors) = LearnModelOptions.Parse(["--run", $"{Key}:intraday:FIXED_RR"]);

        errors.ShouldBeEmpty();
        options!.HoldOutMonths.ShouldBe(6);
        options.Validation.Trials.ShouldBe(1);
        options.Validation.MinFoldsForVerdict.ShouldBe(6);
        options.Required.ShouldBeNull();
    }

    [Fact]
    public void Model_knobs_can_be_set()
    {
        var (options, _) = LearnModelOptions.Parse(["--run", $"{Key}:swing:X", "--hold-out-months", "0", "--trials", "3", "--min-folds", "4", "--require", "cell_n, rsi14"]);

        options!.HoldOutMonths.ShouldBe(0);
        options.Validation.Trials.ShouldBe(3);
        options.Validation.MinFoldsForVerdict.ShouldBe(4);
        options.Required.ShouldBe(["cell_n", "rsi14"]);
    }

    [Theory]
    [InlineData("--run", "k:swing")]
    [InlineData("--trials", "x")]
    [InlineData("--nope", "1")]
    public void A_bad_model_line_is_listed_not_guessed(string flag, string value)
    {
        var (options, errors) = LearnModelOptions.Parse([flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Designate_needs_a_model_a_known_role_and_a_reason()
    {
        var (ok, errors) = LearnDesignateOptions.Parse(["--model", "7", "--role", "champion", "--reason", "proven on six folds"]);
        var (bad, badErrors) = LearnDesignateOptions.Parse(["--model", "0", "--role", "king"]);

        errors.ShouldBeEmpty();
        ok!.ModelId.ShouldBe(7);
        ok.Role.ShouldBe(ModelRole.Champion);
        ok.Reason.ShouldBe("proven on six folds");
        bad.ShouldBeNull();
        badErrors.Count.ShouldBe(3);
    }
}
