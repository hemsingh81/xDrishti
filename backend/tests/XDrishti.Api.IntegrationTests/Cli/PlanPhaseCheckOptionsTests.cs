using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd plan phase-check</c> (spec P7-18): pure parsing, no database.</summary>
public sealed class PlanPhaseCheckOptionsTests
{
    [Fact]
    public void No_arguments_means_the_newest_planned_session_whose_run_is_due()
    {
        var (options, errors) = PlanPhaseCheckOptions.Parse([]);

        errors.ShouldBeEmpty();
        options!.Through.ShouldBeNull();
    }

    [Fact]
    public void A_through_date_is_read_as_a_calendar_date()
    {
        var (options, _) = PlanPhaseCheckOptions.Parse(["--through", "2026-10-23"]);

        options!.Through.ShouldBe(new DateOnly(2026, 10, 23));
    }

    [Theory]
    [InlineData("--through", "23-10-2026")]
    [InlineData("--through", "2026-02-30")]
    [InlineData("--through", "")]
    [InlineData("--date", "2026-10-23")]
    [InlineData("--dry-run", "x")]
    public void A_bad_line_is_refused_with_errors(string flag, string value)
    {
        var (options, errors) = PlanPhaseCheckOptions.Parse([flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_value_or_a_repeated_flag_is_refused()
    {
        PlanPhaseCheckOptions.Parse(["--through"]).Options.ShouldBeNull();
        PlanPhaseCheckOptions.Parse(["--through", "2026-10-23", "--through", "2026-10-22"]).Options.ShouldBeNull();
    }

    [Fact]
    public void The_usage_says_nothing_is_written_and_the_limits_are_printed()
    {
        PlanPhaseCheckOptions.Usage.ShouldContain("nothing is written");
        PlanPhaseCheckOptions.Usage.ShouldContain("never MET on fewer than ten consecutive scheduled sessions");
    }
}
