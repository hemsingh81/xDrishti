using XDrishti.Api.Cli;
using XDrishti.Application.OptionExpression;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd options condors</c> (spec P14-10 contract): pure parsing, no database. A bad line is exit 2 (the usage error); the usage names the exit codes and the gates.</summary>
public sealed class OptionsCondorsCommandTests
{
    private static readonly string Profile = new('b', 64);

    [Fact]
    public void The_verb_takes_a_profile_and_two_dates_and_defaults_to_nifty_and_in_any_order()
    {
        var (plain, errors) = OptionsCondorsCommand.Parse(["--profile", Profile, "--from", "2024-01-01", "--to", "2025-12-31"]);
        var (other, _) = OptionsCondorsCommand.Parse(["--to", "2025-12-31", "--from", "2024-01-01", "--underlying", "NSE:BANKNIFTY", "--profile", Profile]);

        errors.ShouldBeEmpty();
        plain.ShouldBe(new SimulateCondorsCommand(Profile, new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31), "NSE:NIFTY"));
        other!.Underlying.ShouldBe("NSE:BANKNIFTY");
    }

    [Theory]
    [InlineData]
    [InlineData("--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("--profile", "short", "--from", "2024-01-01", "--to", "2024-02-01")]
    [InlineData("--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "--from", "01/01/2024", "--to", "2024-02-01")]
    [InlineData("--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "--from", "2024-01-01")]
    [InlineData("--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "--from", "2024-01-01", "--to", "2024-02-01", "--to", "2024-03-01")]
    [InlineData("--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "--from", "2024-01-01", "--to", "2024-02-01", "--extra")]
    public void A_bad_line_is_a_usage_error(params string[] args)
    {
        var (command, errors) = OptionsCondorsCommand.Parse(args);

        command.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_usage_names_the_exit_codes_and_the_gates()
    {
        OptionsCondorsCommand.Usage.ShouldContain("exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled");
        OptionsCondorsCommand.Usage.ShouldContain("stored regime");
        OptionsCondorsCommand.Usage.ShouldContain("measured half-spread");
    }
}
