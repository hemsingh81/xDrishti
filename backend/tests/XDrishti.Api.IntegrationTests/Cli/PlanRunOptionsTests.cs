using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd plan run</c> (spec P7-08): pure parsing, no database.</summary>
public sealed class PlanRunOptionsTests
{
    [Fact]
    public void No_arguments_mean_the_last_closed_session_for_real()
    {
        var (options, errors) = PlanRunOptions.Parse([]);

        errors.ShouldBeEmpty();
        (options!.Date, options.DryRun).ShouldBe((null, false));
    }

    [Fact]
    public void A_date_and_the_dry_run_flag_are_read()
    {
        var (options, _) = PlanRunOptions.Parse(["--date", "2026-10-23", "--dry-run"]);

        (options!.Date, options.DryRun).ShouldBe((new DateOnly(2026, 10, 23), true));
    }

    [Theory]
    [InlineData("--date")]
    [InlineData("--date", "23-10-2026")]
    [InlineData("--date", "2026-10-23", "--date", "2026-10-22")]
    [InlineData("--dry-run", "--dry-run")]
    [InlineData("--approve")]
    [InlineData("now")]
    public void Anything_else_is_refused(params string[] args)
    {
        var (options, errors) = PlanRunOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }
}
