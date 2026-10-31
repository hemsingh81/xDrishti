using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd exec phase-check</c> (spec P12-12 criterion 12): pure parsing, no database.</summary>
public sealed class ExecPhaseCheckOptionsTests
{
    [Fact]
    public void No_arguments_means_every_account_and_the_default_end()
    {
        var (options, errors) = ExecPhaseCheckOptions.Parse([]);

        errors.ShouldBeEmpty();
        (options!.AccountId, options.Through).ShouldBe((null, null));
    }

    [Fact]
    public void An_account_and_a_through_date_are_read()
    {
        var id = Guid.NewGuid();

        var (options, _) = ExecPhaseCheckOptions.Parse(["--account", id.ToString(), "--through", "2026-10-23"]);

        options!.AccountId.ShouldBe(id);
        options.Through.ShouldBe(new DateOnly(2026, 10, 23));
    }

    [Theory]
    [InlineData("--through", "23-10-2026")]
    [InlineData("--through", "2026-02-30")]
    [InlineData("--account", "main")]
    [InlineData("--date", "2026-10-23")]
    public void A_bad_line_is_refused_with_errors(string flag, string value)
    {
        var (options, errors) = ExecPhaseCheckOptions.Parse([flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_value_or_a_repeated_flag_is_refused()
    {
        ExecPhaseCheckOptions.Parse(["--through"]).Options.ShouldBeNull();
        ExecPhaseCheckOptions.Parse(["--through", "2026-10-23", "--through", "2026-10-24"]).Options.ShouldBeNull();
    }

    [Fact]
    public void The_usage_names_the_exit_codes_and_that_nothing_is_written() => ExecPhaseCheckOptions.Usage.ShouldContain("exit codes: 0 done");
}
