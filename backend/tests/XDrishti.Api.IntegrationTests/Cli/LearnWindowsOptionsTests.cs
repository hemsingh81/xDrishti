using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn windows</c> (spec P6-14): pure parsing, no database.</summary>
public sealed class LearnWindowsOptionsTests
{
    private static readonly string Key = new('a', 64);

    [Fact]
    public void Defaults_apply_for_what_is_left_out()
    {
        var (options, errors) = LearnWindowsOptions.Parse(["--run", $"{Key}:intraday:FIXED_RR"]);

        errors.ShouldBeEmpty();
        options!.Sources.Count.ShouldBe(1);
        options.Options.BucketMinutes.ShouldBe(30);
        options.Options.MinWindowTrades.ShouldBe(30);
        options.HoldOutMonths.ShouldBe(6);
    }

    [Fact]
    public void The_knobs_can_be_set()
    {
        var (options, _) = LearnWindowsOptions.Parse(["--run", $"{Key}:swing:X", "--bucket-minutes", "60", "--min-trades", "40", "--min-lift", "0.05", "--prior-strength", "10", "--hold-out-months", "0"]);

        options!.Options.BucketMinutes.ShouldBe(60);
        options.Options.MinWindowTrades.ShouldBe(40);
        options.Options.MinLift.ShouldBe(0.05);
        options.Options.PriorStrength.ShouldBe(10);
        options.HoldOutMonths.ShouldBe(0);
    }

    [Theory]
    [InlineData("--run", "k:swing")]
    [InlineData("--bucket-minutes", "x")]
    [InlineData("--min-lift", "NaN")]
    [InlineData("--nope", "1")]
    public void A_bad_line_is_listed_not_guessed(string flag, string value)
    {
        var (options, errors) = LearnWindowsOptions.Parse(flag == "--run" ? [flag, value] : ["--run", $"{Key}:swing:X", flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Without_a_run_the_line_is_refused()
    {
        LearnWindowsOptions.Parse(["--min-trades", "5"]).Options.ShouldBeNull();
    }
}
