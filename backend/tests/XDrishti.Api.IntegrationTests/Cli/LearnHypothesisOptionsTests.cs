using XDrishti.Api.Cli;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn hypothesis</c> (spec P6-10): pure parsing, no database.</summary>
public sealed class LearnHypothesisOptionsTests
{
    private static readonly string A = new('a', 64);

    [Fact]
    public void Propose_parses_one_knob_with_the_owner_as_the_default_source()
    {
        var (options, errors) = LearnHypothesisOptions.Parse(["propose", "--baseline", A, "--knob", "rr", "--kind", "parameter", "--from", "2", "--to", "2.4", "--reason", "MFE"]);

        errors.ShouldBeEmpty();
        options!.Source.ShouldBe(HypothesisSource.Owner);
        options.Knob!.Name.ShouldBe("rr");
        options.Reason.ShouldBe("MFE");
    }

    [Fact]
    public void The_grid_source_and_grid_knobs_are_read()
    {
        var (options, _) = LearnHypothesisOptions.Parse(["propose", "--source", "grid", "--grid-step-pct", "5", "--grid-steps", "3", "--baseline", A, "--knob", "rr", "--kind", "parameter", "--from", "2", "--to", "2.1", "--reason", "g"]);

        options!.Source.ShouldBe(HypothesisSource.Grid);
        options.Grid.ShouldBe(new HypothesisGrid(5, 3));
    }

    [Theory]
    [InlineData("approve", "--id", "4", "--reason", "ok")]
    [InlineData("reject", "--id", "4", "--reason", "no")]
    [InlineData("start", "--id", "4", "--candidate", "b")]
    [InlineData("judge", "--id", "4", "--hold-out-months", "0", "--trials", "2")]
    [InlineData("grid", "--baseline", "a", "--parameter", "rr")]
    public void Each_action_parses_its_own_options(params string[] args)
    {
        var (options, errors) = LearnHypothesisOptions.Parse(args);

        errors.ShouldBeEmpty();
        options!.Action.ShouldBe(args[0]);
    }

    [Fact]
    public void A_stray_knob_option_on_another_action_does_not_crash_the_parser()
    {
        var (options, errors) = LearnHypothesisOptions.Parse(["approve", "--id", "4", "--reason", "ok", "--knob", "rr"]);

        errors.ShouldBeEmpty();
        options!.Knob.ShouldBeNull();
    }

    [Fact]
    public void Triggers_take_runs_and_a_date()
    {
        var (options, errors) = LearnHypothesisOptions.Parse(["triggers", "--run", A + ":intraday:FIXED_RR", "--since", "2026-05-01", "--min-new-trades", "20"]);

        errors.ShouldBeEmpty();
        options!.Sources.Single().Style.ShouldBe("intraday");
        options.Since.ShouldBe(new DateOnly(2026, 5, 1));
        options.MinNewTrades.ShouldBe(20);
    }

    [Theory]
    [InlineData]
    [InlineData("wish")]
    [InlineData("approve", "--id", "4")]
    [InlineData("approve", "--id", "x", "--reason", "r")]
    [InlineData("propose", "--source", "llm", "--baseline", "a", "--knob", "k", "--kind", "parameter", "--from", "1", "--to", "2", "--reason", "r")]
    [InlineData("propose", "--baseline", "a", "--knob", "k", "--kind", "wish", "--from", "1", "--to", "2", "--reason", "r")]
    [InlineData("judge", "--id", "4", "--id", "5")]
    [InlineData("judge", "--id", "4", "--bogus", "1")]
    [InlineData("triggers", "--since", "2026-05-01")]
    [InlineData("triggers", "--run", "a:b", "--since", "2026-05-01")]
    [InlineData("triggers", "--run", "a:intraday:X", "--since", "May")]
    public void A_bad_line_is_refused_with_reasons(params string[] args)
    {
        var (options, errors) = LearnHypothesisOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }
}
