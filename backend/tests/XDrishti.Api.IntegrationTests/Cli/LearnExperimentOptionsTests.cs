using XDrishti.Api.Cli;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn experiment</c> (spec P6-11): pure parsing, no database.</summary>
public sealed class LearnExperimentOptionsTests
{
    private static readonly string A = new('a', 64);

    private static readonly string B = new('b', 64);

    private static string[] Line(params string[] extra) =>
        [.. new[] { "--baseline", A, "--candidate", B, "--knob", "slippage_bps", "--kind", "slippage", "--from", "8", "--to", "4" }, .. extra];

    [Fact]
    public void Defaults_keep_a_six_month_hold_out_and_one_trial()
    {
        var (options, errors) = LearnExperimentOptions.Parse(Line());

        errors.ShouldBeEmpty();
        options!.HoldOutMonths.ShouldBe(6);
        options.Options.Trials.ShouldBe(1);
        options.Knob.Kind.ShouldBe(ExperimentKnobKind.Slippage);
    }

    [Fact]
    public void Knobs_can_be_set()
    {
        var (options, _) = LearnExperimentOptions.Parse(Line("--hold-out-months", "0", "--min-fold-trades", "5", "--live-gap-r", "0.05", "--trials", "3"));

        options!.HoldOutMonths.ShouldBe(0);
        options.Options.MinFoldTrades.ShouldBe(5);
        options.Options.ExpectedLiveGapR.ShouldBe(0.05);
        options.Options.Trials.ShouldBe(3);
    }

    [Theory]
    [InlineData("--kind", "wish")]
    [InlineData("--trials", "many")]
    [InlineData("--live-gap-r", "NaN")]
    [InlineData("--unknown", "1")]
    public void A_bad_line_is_refused_with_errors(string flag, string value)
    {
        var args = Line().ToList();
        var at = args.IndexOf(flag);
        if (at >= 0)
        {
            args[at + 1] = value;
        }
        else
        {
            args.AddRange([flag, value]);
        }

        var (options, errors) = LearnExperimentOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_required_value_is_named()
    {
        var (options, errors) = LearnExperimentOptions.Parse(["--baseline", A]);

        options.ShouldBeNull();
        errors.ShouldContain("give --candidate");
        errors.ShouldContain("give --to");
    }
}
