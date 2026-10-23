using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn loss-tags</c> (spec P6-15): pure parsing, no database.</summary>
public sealed class LearnLossTagsOptionsTests
{
    private static readonly string Key = new('a', 64);

    [Fact]
    public void Defaults_apply_for_what_is_left_out()
    {
        var (options, errors) = LearnLossTagsOptions.Parse(["--run", $"{Key}:intraday:FIXED_RR"]);

        errors.ShouldBeEmpty();
        options!.Sources.Count.ShouldBe(1);
        options.Options.GaveBackMfeR.ShouldBe(1.0);
        options.Options.GrowingWindow.ShouldBe(30);
    }

    [Fact]
    public void The_knobs_can_be_set()
    {
        var (options, _) = LearnLossTagsOptions.Parse(["--run", $"{Key}:swing:X", "--gap-min-r", "0.2", "--gave-back-mfe-r", "1.5", "--high-slippage-r", "0.3", "--late-after-minute", "900", "--wide-stop-fraction", "0.02", "--growing-window", "20", "--show-worst", "3"]);

        options!.Options.GapMinR.ShouldBe(0.2);
        options.Options.GaveBackMfeR.ShouldBe(1.5);
        options.Options.HighSlippageR.ShouldBe(0.3);
        options.Options.LateAfterMinute.ShouldBe(900);
        options.Options.WideStopFraction.ShouldBe(0.02);
        options.Options.GrowingWindow.ShouldBe(20);
        options.Options.ShowWorst.ShouldBe(3);
    }

    [Theory]
    [InlineData("--run", "k:swing")]
    [InlineData("--gap-min-r", "x")]
    [InlineData("--growing-window", "1.5")]
    [InlineData("--gave-back-mfe-r", "NaN")]
    [InlineData("--nope", "1")]
    public void A_bad_line_is_listed_not_guessed(string flag, string value)
    {
        var (options, errors) = LearnLossTagsOptions.Parse(flag == "--run" ? [flag, value] : ["--run", $"{Key}:swing:X", flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Without_a_run_the_line_is_refused()
    {
        LearnLossTagsOptions.Parse(["--show-worst", "5"]).Options.ShouldBeNull();
    }
}
