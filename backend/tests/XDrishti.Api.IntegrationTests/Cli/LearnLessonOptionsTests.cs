using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn lesson</c> (spec P6-16).</summary>
public sealed class LearnLessonOptionsTests
{
    private static readonly string Run = new string('a', 64) + ":intraday:FIXED_RR";

    [Fact]
    public void A_scan_takes_runs_months_a_model_and_a_dry_run_flag()
    {
        var (options, errors) = LearnLessonOptions.Parse(["scan", "--run", Run, "--monitor-months", "3", "--model", "9", "--dry-run"]);

        errors.ShouldBeEmpty();
        options.ShouldNotBeNull();
        (options.Action, options.Sources.Count, options.MonitorMonths, options.ModelId, options.DryRun).ShouldBe(("scan", 1, 3, 9L, true));
    }

    [Fact]
    public void Link_takes_exactly_one_fix_and_a_reason()
    {
        var (hypothesis, _) = LearnLessonOptions.Parse(["link", "--id", "4", "--hypothesis", "7", "--reason", "tried"]);
        var (experiment, _) = LearnLessonOptions.Parse(["link", "--id", "4", "--experiment", "8", "--reason", "tried"]);

        (hypothesis!.FixKind, hypothesis.FixId, hypothesis.Id).ShouldBe(("hypothesis", 7L, 4L));
        (experiment!.FixKind, experiment.FixId).ShouldBe(("experiment", 8L));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope --id 1")]
    [InlineData("scan")]
    [InlineData("scan --run bad")]
    [InlineData("scan --run X:intraday:E --monitor-months x")]
    [InlineData("scan --run X:intraday:E --id 1")]
    [InlineData("scan --run X:intraday:E --reason r")]
    [InlineData("scan --run X:intraday:E --hypothesis 2")]
    [InlineData("scan --run X:intraday:E --experiment 2")]
    [InlineData("scan --run X:intraday:E --monitor-months 0")]
    [InlineData("scan --run X:intraday:E --monitor-months 121")]
    [InlineData("scan --run X:intraday:E --monitor-months 4294967302")]
    [InlineData("link --id 1 --reason r")]
    [InlineData("link --id 1 --hypothesis 2 --experiment 3 --reason r")]
    [InlineData("link --hypothesis 2 --reason r")]
    [InlineData("close --id 1")]
    [InlineData("close --id 1 --reason r --dry-run")]
    [InlineData("dismiss --id one --reason r")]
    [InlineData("close --id 1 --id 2 --reason r")]
    [InlineData("close --id 1 --hypothesis 2 --reason r")]
    public void A_bad_line_is_refused_with_errors(string line)
    {
        var (options, errors) = LearnLessonOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }
}
