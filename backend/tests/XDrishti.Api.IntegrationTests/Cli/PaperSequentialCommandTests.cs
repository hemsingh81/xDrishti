using XDrishti.Api.Cli;
using XDrishti.Application.Shadow;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line and the text of <c>xd paper sequential</c> (spec CF-09): pure, no database.</summary>
public sealed class PaperSequentialCommandTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    [Fact]
    public void The_verb_is_found_by_its_name()
    {
        PaperSequentialCommand.Handles(["sequential"]).ShouldBeTrue();
        PaperSequentialCommand.Handles(["shadow", "summary"]).ShouldBeFalse();
    }

    [Fact]
    public void No_options_means_the_system_scope()
    {
        PaperSequentialCommand.TryParse([], out var scope, out var strategy).ShouldBeTrue();

        (scope, strategy).ShouldBe(("system", null));
    }

    [Fact]
    public void The_strategy_scope_needs_a_strategy_and_the_system_scope_takes_none()
    {
        PaperSequentialCommand.TryParse(["--scope", "strategy", "--strategy", "ORB"], out var scope, out var strategy).ShouldBeTrue();
        (scope, strategy).ShouldBe(("strategy", "ORB"));

        PaperSequentialCommand.TryParse(["--scope", "strategy"], out _, out _).ShouldBeFalse();
        PaperSequentialCommand.TryParse(["--strategy", "ORB"], out _, out _).ShouldBeFalse();
        PaperSequentialCommand.TryParse(["--scope", "system", "--strategy", "ORB"], out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("--scope", "all")]
    [InlineData("--scope", "")]
    [InlineData("--epoch", "1")]
    public void A_bad_line_is_a_usage_error(string flag, string value) => PaperSequentialCommand.TryParse([flag, value], out _, out _).ShouldBeFalse();

    [Fact]
    public void A_missing_value_or_a_repeated_flag_is_a_usage_error()
    {
        PaperSequentialCommand.TryParse(["--scope"], out _, out _).ShouldBeFalse();
        PaperSequentialCommand.TryParse(["--scope", "system", "--scope", "system"], out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_text_names_the_state_the_boundaries_the_first_crossing_the_basis_and_the_honest_statements()
    {
        var edge = new SequentialSeriesView("gross R, modelled fills; no queue, no latency", null, "failure", "failure: the evidence favoured a mean near 0", -3.03, 31, 20.5, 16, Day, 31, Day.AddDays(1), 0, null, new SequentialPathView(-3.03, 31, 1.5));
        var behavioural = new SequentialSeriesView("P10 behavioural series", "No counted paper trade in the epoch yet.", "monitoring", "monitoring: the evidence does not yet favour either side", 0, 0, 0, null, null, null, null, 0, null, new SequentialPathView(0, 0, 1.5));
        var view = new SequentialStatusView("system", null, 3, true, new SequentialBoundariesView(-1.558145, -2.944439, 2.772589, 4.75, 19), 0.3, true, new string('a', 64), "mu1=0.2", edge, behavioural, ["Gaussian model.", "report only, never a pass"], "line text");

        var text = string.Join('\n', PaperSequentialCommand.Lines(view));

        text.ShouldStartWith("line text");
        text.ShouldContain("paper epoch 3");
        text.ShouldContain("caution -1.558145 (odds 4.75 to 1), failure -2.944439 (odds 19 to 1)");
        text.ShouldContain("edge series: failure: the evidence favoured a mean near 0");
        text.ShouldContain("first at or below the failure boundary: trade 31 (2026-11-06); the flag stays for this epoch");
        text.ShouldContain("P10 behavioural series: monitoring");
        text.ShouldContain("No counted paper trade in the epoch yet.");
        text.ShouldContain("report only, never a pass");
    }
}
