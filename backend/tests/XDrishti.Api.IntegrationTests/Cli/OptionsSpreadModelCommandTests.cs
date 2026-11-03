using XDrishti.Api.Cli;
using XDrishti.Application.OptionCosts;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd options spread-model build</c> (spec P14-07 contract): pure parsing, no database. A bad line is exit 2 (the usage error).</summary>
public sealed class OptionsSpreadModelCommandTests
{
    [Fact]
    public void The_verb_builds_every_underlying_or_the_one_named()
    {
        var (all, allErrors) = OptionsSpreadModelCommand.Parse(["build"]);
        var (one, oneErrors) = OptionsSpreadModelCommand.Parse(["build", "--underlying", "NSE:NIFTY"]);

        allErrors.ShouldBeEmpty();
        all.ShouldBe(new BuildSpreadModelCommand(null));
        oneErrors.ShouldBeEmpty();
        one.ShouldBe(new BuildSpreadModelCommand("NSE:NIFTY"));
    }

    [Theory]
    [InlineData]
    [InlineData("make")]
    [InlineData("build", "--underlying")]
    [InlineData("build", "--underlying", "")]
    [InlineData("build", "--underlying", "NSE:NIFTY", "--underlying", "NSE:BANKNIFTY")]
    [InlineData("build", "--sessions", "60")]
    [InlineData("build", "NIFTY")]
    [InlineData("build", "--underlying", "NSE:NIF\u0007TY")]
    public void A_bad_line_is_a_usage_error(params string[] args)
    {
        var (command, errors) = OptionsSpreadModelCommand.Parse(args);

        command.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_usage_text_names_the_exit_codes_and_the_global_usage_lists_the_verb()
    {
        OptionsSpreadModelCommand.Usage.ShouldContain("exit codes: 0 built or nothing to build, 2 wrong command line, 3 failure, 130 cancelled");
        XdOptions.Usage.ShouldContain("xd options spread-model build [--underlying SYMBOL]");
    }
}
