using XDrishti.Api.Cli;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn policy</c> (spec P6-13): pure parsing, no database.</summary>
public sealed class LearnPolicyOptionsTests
{
    private static readonly string Key = new('a', 64);

    [Fact]
    public void Defaults_apply_for_what_is_left_out()
    {
        var (options, errors) = LearnPolicyOptions.Parse(["--run", $"{Key}:intraday:FIXED_RR"]);

        errors.ShouldBeEmpty();
        options!.Sources.Count.ShouldBe(1);
        options.Policy.Split.ShouldBe(MetaSplit.None);
        options.Policy.MinKeptShare.ShouldBe(0.5);
        options.HoldOutMonths.ShouldBe(6);
    }

    [Fact]
    public void The_knobs_can_be_set()
    {
        var (options, _) = LearnPolicyOptions.Parse(["--run", $"{Key}:swing:X", "--split", "style-side", "--min-kept-share", "0.6", "--min-lift-r", "0.1", "--min-skill-r", "0.2", "--trials", "5", "--hold-out-months", "0"]);

        options!.Policy.Split.ShouldBe(MetaSplit.StyleSide);
        options.Policy.MinKeptShare.ShouldBe(0.6);
        options.Policy.MinLiftR.ShouldBe(0.1);
        options.Policy.MinSkillR.ShouldBe(0.2);
        options.Policy.Trials.ShouldBe(5);
        options.HoldOutMonths.ShouldBe(0);
    }

    [Theory]
    [InlineData("--split", "weekly")]
    [InlineData("--min-kept-share", "x")]
    [InlineData("--min-lift-r", "NaN")]
    [InlineData("--hold-out-months", "x")]
    [InlineData("--trials", "x")]
    [InlineData("--min-skill-r", "NaN")]
    [InlineData("--nope", "1")]
    public void A_bad_line_is_listed_not_guessed(string flag, string value)
    {
        var (options, errors) = LearnPolicyOptions.Parse(["--run", $"{Key}:swing:X", flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Without_a_run_the_line_is_refused()
    {
        LearnPolicyOptions.Parse(["--split", "side"]).Options.ShouldBeNull();
    }
}
