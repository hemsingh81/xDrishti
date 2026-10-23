using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn phase-check</c> (spec P6-18): pure parsing, no database.</summary>
public sealed class LearnPhaseCheckOptionsTests
{
    [Fact]
    public void No_arguments_means_no_replay_time()
    {
        var (options, errors) = LearnPhaseCheckOptions.Parse([]);

        errors.ShouldBeEmpty();
        options!.ReplaySeconds.ShouldBeNull();
    }

    [Fact]
    public void A_replay_time_is_read_with_the_invariant_culture()
    {
        var (options, _) = LearnPhaseCheckOptions.Parse(["--replay-seconds", "312.5"]);

        options!.ReplaySeconds.ShouldBe(312.5);
    }

    [Theory]
    [InlineData("--replay-seconds", "0")]
    [InlineData("--replay-seconds", "-1")]
    [InlineData("--replay-seconds", "NaN")]
    [InlineData("--replay-seconds", "fast")]
    [InlineData("--unknown", "1")]
    public void A_bad_line_is_refused_with_errors(string flag, string value)
    {
        var (options, errors) = LearnPhaseCheckOptions.Parse([flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_value_or_a_repeated_flag_is_refused()
    {
        LearnPhaseCheckOptions.Parse(["--replay-seconds"]).Options.ShouldBeNull();
        LearnPhaseCheckOptions.Parse(["--replay-seconds", "1", "--replay-seconds", "2"]).Options.ShouldBeNull();
    }
}
