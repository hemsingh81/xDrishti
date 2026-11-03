using XDrishti.Api.Cli;
using XDrishti.Application.OptionExpression;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd options remark</c> (spec P14-08 contract): pure parsing, no database. A bad line is exit 2 (the usage error); the usage names the exit codes.</summary>
public sealed class OptionsRemarkCommandTests
{
    private static readonly string Run = new('a', 64);
    private static readonly string Profile = new('b', 64);

    [Fact]
    public void The_verb_takes_a_run_key_and_a_profile_hash_in_either_order()
    {
        var (one, errors) = OptionsRemarkCommand.Parse(["--run", Run, "--profile", Profile]);
        var (other, _) = OptionsRemarkCommand.Parse(["--profile", Profile, "--run", Run]);

        errors.ShouldBeEmpty();
        one.ShouldBe(new RemarkRunCommand(Run, Profile));
        other.ShouldBe(one);
    }

    [Theory]
    [InlineData]
    [InlineData("--run")]
    [InlineData("--run", "short", "--profile", "also-short")]
    [InlineData("--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("--run", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("--run", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "--extra")]
    [InlineData("--run", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "--run", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "--profile", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public void A_bad_line_is_a_usage_error(params string[] args)
    {
        var (command, errors) = OptionsRemarkCommand.Parse(args);

        command.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_usage_texts_name_the_exit_codes_and_the_global_usage_lists_the_verb()
    {
        OptionsRemarkCommand.Usage.ShouldContain("exit codes: 0 done, 1 unknown run or profile, 2 wrong command line, 3 failure, 130 cancelled");
        XdOptions.Usage.ShouldContain("xd options remark --run RUNKEY --profile HASH");
    }
}
