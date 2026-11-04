using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd options phase-check</c> (spec P14-17): pure parsing, no database. Nothing is cut unless asked.</summary>
public sealed class OptionsPhaseCheckOptionsTests
{
    [Fact]
    public void No_arguments_cut_nothing_and_print_text()
    {
        var (options, errors) = OptionsPhaseCheckOptions.Parse([]);

        errors.ShouldBeEmpty();
        options!.Cut.ShouldBeEmpty();
        options.Json.ShouldBeFalse();
    }

    [Fact]
    public void A_cut_list_and_the_json_flag_are_read_in_any_order_and_a_family_named_twice_counts_once()
    {
        var (options, _) = OptionsPhaseCheckOptions.Parse(["--json", "--cut", "expr:iron_condor, basis:carry-1,basis:carry-1"]);

        options!.Cut.ShouldBe(["expr:iron_condor", "basis:carry-1"]);
        options.Json.ShouldBeTrue();
    }

    [Theory]
    [InlineData("--through", "2026-11-03")]
    [InlineData("--jsn", "x")]
    public void An_unknown_flag_is_refused_with_errors(string flag, string value)
    {
        var (options, errors) = OptionsPhaseCheckOptions.Parse([flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_value_or_a_repeated_flag_is_refused()
    {
        OptionsPhaseCheckOptions.Parse(["--cut"]).Options.ShouldBeNull();
        OptionsPhaseCheckOptions.Parse(["--cut", "basis:carry-1", "--cut", "ext:iv-1"]).Options.ShouldBeNull();
        OptionsPhaseCheckOptions.Parse(["--json", "--json"]).Options.ShouldBeNull();
    }

    [Fact]
    public void The_usage_says_nothing_is_written_and_exit_is_zero_whatever_the_verdict()
    {
        OptionsPhaseCheckOptions.Usage.ShouldContain("nothing is written");
        OptionsPhaseCheckOptions.Usage.ShouldContain("no switch or setting is changed");
        OptionsPhaseCheckOptions.Usage.ShouldContain("basis:carry-1");
        OptionsPhaseCheckOptions.Usage.ShouldContain("0 done (whatever the verdict)");
    }
}
