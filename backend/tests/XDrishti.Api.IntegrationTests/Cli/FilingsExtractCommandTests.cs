using XDrishti.Api.Cli;
using XDrishti.Application.Filings;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd filings extract</c> (spec P15-08 criterion 5): pure parsing, no database, no model.</summary>
public sealed class FilingsExtractCommandTests
{
    [Fact]
    public void The_two_dates_are_required_and_the_limit_is_optional()
    {
        var (command, errors) = FilingsExtractCommand.Parse(["--from", "2026-07-01", "--to", "2026-07-31"]);
        var (limited, _) = FilingsExtractCommand.Parse(["--from", "2026-07-01", "--to", "2026-07-31", "--limit", "25"]);

        errors.ShouldBeEmpty();
        command.ShouldBe(new ExtractAnnouncementsCommand(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), null));
        limited.ShouldBe(new ExtractAnnouncementsCommand(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 25));
    }

    [Theory]
    [InlineData("--from", "2026-07-01")]
    [InlineData("--to", "2026-07-31")]
    public void A_missing_date_is_a_usage_error(string name, string value)
    {
        var (command, errors) = FilingsExtractCommand.Parse([name, value]);

        command.ShouldBeNull();
        errors.ShouldContain("--from and --to are required");
    }

    [Theory]
    [InlineData("--from", "01-07-2026")]
    [InlineData("--from", "2026-13-01")]
    [InlineData("--limit", "0")]
    [InlineData("--limit", "-3")]
    [InlineData("--limit", "ten")]
    [InlineData("--model", "x")]
    [InlineData("--from", "")]
    public void A_bad_value_or_an_unknown_argument_is_a_usage_error(string name, string value)
    {
        var (command, errors) = FilingsExtractCommand.Parse(["--from", "2026-07-01", "--to", "2026-07-31", name, value]);

        command.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_usage_text_names_the_switch_and_the_exit_codes_and_the_global_usage_lists_the_verb()
    {
        FilingsExtractCommand.Usage.ShouldContain("assistant.disabled");
        FilingsExtractCommand.Usage.ShouldContain("exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled");
        XdOptions.Usage.ShouldContain("xd filings extract --from YYYY-MM-DD --to YYYY-MM-DD [--limit N]");
    }
}
