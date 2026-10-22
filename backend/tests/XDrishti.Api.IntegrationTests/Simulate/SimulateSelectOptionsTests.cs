using XDrishti.Api.Simulate;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>Criterion 1 and 10 of P4-06 at the command line: <c>--select-exit</c> and its overrides need a comparison, are echoed into the options and each refusal is a usage error that names the flag; without the flag the options are the options of before.</summary>
public sealed class SimulateSelectOptionsTests
{
    private static readonly string[] Known = ["ORB"];

    private const string Base = "--strategy ORB --symbols RELIANCE --from 2026-03-02 --to 2026-03-04 --gross --exit FIXED_RR:rr=1.5|2|2.5|3";

    private static SimulateParse Parse(string line) => SimulateOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), Known);

    [Fact]
    public void Select_exit_alone_takes_the_defaults_of_doc_16()
    {
        var parsed = Parse(Base + " --select-exit");

        parsed.Errors.ShouldBeEmpty();
        parsed.Options!.Selection.ShouldBe(new SelectionOptions());
        parsed.Options.Exits!.Candidates.Count.ShouldBe(4);
    }

    [Fact]
    public void The_overrides_are_read_into_the_options()
    {
        var parsed = Parse(Base + " --select-exit --min-train-months 6 --test-months 2 --embargo-sessions 3 --lambda 0.25 --min-trades 20 --no-plateau");

        parsed.Errors.ShouldBeEmpty();
        parsed.Options!.Selection.ShouldBe(new SelectionOptions(6, 2, 3, 0.25, 20, false));
    }

    [Fact]
    public void Without_select_exit_the_options_are_the_options_of_before()
    {
        Parse(Base).Options!.Selection.ShouldBeNull();
    }

    [Theory]
    [InlineData("--strategy ORB --symbols RELIANCE --from 2026-03-02 --to 2026-03-04 --gross --select-exit", "two candidates or more")]
    [InlineData("--strategy ORB --symbols RELIANCE --from 2026-03-02 --to 2026-03-04 --gross --exit FIXED_RR:rr=2 --select-exit", "two candidates or more")]
    [InlineData(Base + " --lambda 0.5", "'--lambda' needs --select-exit")]
    [InlineData(Base + " --no-plateau", "'--no-plateau' needs --select-exit")]
    [InlineData(Base + " --min-trades 20", "'--min-trades' needs --select-exit")]
    [InlineData(Base + " --select-exit --lambda abc", "'--lambda' must be a finite number")]
    [InlineData(Base + " --select-exit --test-months x", "'--test-months' must be a whole number")]
    [InlineData(Base + " --select-exit --min-trades 1", "min_trades must be at least 2")]
    [InlineData(Base + " --select-exit --test-months 0", "test_months must be 1 to 60")]
    public void Each_refusal_is_a_usage_error_that_names_the_flag(string line, string message)
    {
        var parsed = Parse(line);

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldContain(e => e.Contains(message, StringComparison.Ordinal));
    }
}
