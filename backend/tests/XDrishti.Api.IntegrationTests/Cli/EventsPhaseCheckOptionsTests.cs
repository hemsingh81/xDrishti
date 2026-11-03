using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd events phase-check</c> (spec P15-12): pure parsing, no database. Nothing is cut unless asked.</summary>
public sealed class EventsPhaseCheckOptionsTests
{
    [Fact]
    public void No_arguments_cut_nothing_and_print_text()
    {
        var (options, errors) = EventsPhaseCheckOptions.Parse([]);

        errors.ShouldBeEmpty();
        options!.Cut.ShouldBeEmpty();
        options.Json.ShouldBeFalse();
    }

    [Fact]
    public void A_cut_list_and_the_json_flag_are_read_in_any_order_and_a_family_named_twice_counts_once()
    {
        var (options, _) = EventsPhaseCheckOptions.Parse(["--json", "--cut", "ext:llm-1, pead,pead"]);

        options!.Cut.ShouldBe(["ext:llm-1", "pead"]);
        options.Json.ShouldBeTrue();
    }

    [Theory]
    [InlineData("--through", "2026-11-03")]
    [InlineData("--jsn", "x")]
    public void An_unknown_flag_is_refused_with_errors(string flag, string value)
    {
        var (options, errors) = EventsPhaseCheckOptions.Parse([flag, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_value_or_a_repeated_flag_is_refused()
    {
        EventsPhaseCheckOptions.Parse(["--cut"]).Options.ShouldBeNull();
        EventsPhaseCheckOptions.Parse(["--cut", "pead", "--cut", "ext:ev-1"]).Options.ShouldBeNull();
        EventsPhaseCheckOptions.Parse(["--json", "--json"]).Options.ShouldBeNull();
    }

    [Fact]
    public void The_usage_says_nothing_is_written_and_exit_is_zero_whatever_the_verdict()
    {
        EventsPhaseCheckOptions.Usage.ShouldContain("nothing is written");
        EventsPhaseCheckOptions.Usage.ShouldContain("no model is called");
        EventsPhaseCheckOptions.Usage.ShouldContain("0 done (whatever the verdict)");
    }
}
