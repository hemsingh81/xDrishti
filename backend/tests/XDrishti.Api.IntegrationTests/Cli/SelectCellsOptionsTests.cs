using XDrishti.Api.Cli;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd select cells</c> (spec P7-01): pure parsing, no database.</summary>
public sealed class SelectCellsOptionsTests
{
    [Fact]
    public void A_date_alone_means_the_candidate_minimum()
    {
        var (options, errors) = SelectCellsOptions.Parse(["--date", "2026-10-26"]);

        errors.ShouldBeEmpty();
        options!.Date.ShouldBe(new DateOnly(2026, 10, 26));
        options.MinState.ShouldBe(CellState.Candidate);
    }

    [Fact]
    public void A_minimum_state_is_read()
    {
        var (options, _) = SelectCellsOptions.Parse(["--date", "2026-10-26", "--min-state", "active"]);

        options!.MinState.ShouldBe(CellState.Active);
    }

    [Theory]
    [InlineData("--min-state", "research")]
    [InlineData("--min-state", "paused")]
    [InlineData("--min-state", "x")]
    [InlineData("--date", "26/10/2026")]
    [InlineData("--unknown", "1")]
    public void Bad_arguments_are_listed(string name, string value)
    {
        var (options, errors) = SelectCellsOptions.Parse(name == "--date" ? [name, value] : ["--date", "2026-10-26", name, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_date_and_a_repeated_option_are_refused()
    {
        SelectCellsOptions.Parse([]).Options.ShouldBeNull();
        SelectCellsOptions.Parse(["--date", "2026-10-26", "--date", "2026-10-27"]).Options.ShouldBeNull();
    }
}
