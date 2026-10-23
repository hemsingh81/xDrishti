using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn drift</c> (spec P6-09): pure parsing, no database.</summary>
public sealed class LearnDriftOptionsTests
{
    private static readonly string Key = new('a', 64);

    [Fact]
    public void Defaults_apply_for_what_is_left_out()
    {
        var (options, errors) = LearnDriftOptions.Parse(["--run", $"{Key}:intraday:FIXED_RR"]);

        errors.ShouldBeEmpty();
        options!.MonitorMonths.ShouldBe(6);
        options.ModelId.ShouldBeNull();
        options.Options.WindowTrades.ShouldBe(20);
        options.Options.BelowBandChecks.ShouldBe(3);
    }

    [Fact]
    public void The_knobs_and_a_model_can_be_set()
    {
        var (options, _) = LearnDriftOptions.Parse(["--run", $"{Key}:swing:X", "--monitor-months", "3", "--model", "7", "--require", "a,b", "--window-trades", "30", "--below-band-checks", "2", "--clear-checks", "4", "--check-every", "10", "--band-z", "2", "--cusum-allowance", "0.4", "--cusum-threshold", "9", "--calibration-window", "80", "--max-calibration-gap-pp", "6"]);

        options!.MonitorMonths.ShouldBe(3);
        options.ModelId.ShouldBe(7);
        options.Required.ShouldBe(["a", "b"]);
        options.Options.WindowTrades.ShouldBe(30);
        options.Options.CusumThreshold.ShouldBe(9);
        options.Options.MaxCalibrationGapPp.ShouldBe(6);
    }

    [Theory]
    [InlineData("--run", "k:swing")]
    [InlineData("--window-trades", "x")]
    [InlineData("--band-z", "NaN")]
    [InlineData("--model", "1.5")]
    [InlineData("--require", "a")]
    [InlineData("--nope", "1")]
    public void Wrong_input_lists_errors_and_no_options(string flag, string value)
    {
        var (options, errors) = LearnDriftOptions.Parse(["--run", $"{Key}:swing:X", flag, value]);

        if (flag == "--run")
        {
            (options, errors) = LearnDriftOptions.Parse([flag, value]);
        }

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_run_is_required_and_a_flag_twice_is_refused()
    {
        LearnDriftOptions.Parse([]).Options.ShouldBeNull();
        LearnDriftOptions.Parse(["--run", $"{Key}:swing:X", "--band-z", "1", "--band-z", "2"]).Options.ShouldBeNull();
    }
}
