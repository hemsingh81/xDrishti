using XDrishti.Api.Cli;
using XDrishti.Application.OptionExpression;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd options expression-eval</c> (spec P14-09 contract): pure parsing, no database. A bad line is exit 2 (the usage error); an unknown family is the use case's refusal (exit 1).</summary>
public sealed class OptionsExpressionEvalCommandTests
{
    private static readonly string Run = new('a', 64);
    private static readonly string Profile = new('b', 64);

    [Fact]
    public void The_verb_takes_the_family_the_run_and_optionally_the_profile_and_the_hold_out_flag()
    {
        var plain = OptionsExpressionEvalCommand.Parse(["--family", "expr:option_buy", "--run", Run]);
        var full = OptionsExpressionEvalCommand.Parse(["--holdout", "--family", "expr:option_buy", "--profile", Profile, "--run", Run]);

        plain.Errors.ShouldBeEmpty();
        plain.Command.ShouldBe(new EvaluateExpressionCommand("expr:option_buy", Run, null, false));
        full.Command.ShouldBe(new EvaluateExpressionCommand("expr:option_buy", Run, Profile, true));
    }

    [Fact]
    public void An_unknown_family_parses_and_is_refused_by_the_use_case()
    {
        OptionsExpressionEvalCommand.Parse(["--family", "pead", "--run", Run]).Command.ShouldNotBeNull();
    }

    [Theory]
    [InlineData]
    [InlineData("--family", "expr:option_buy")]
    [InlineData("--run", "abc")]
    [InlineData("--family", "expr:option_buy", "--run", "ABCDEF")]
    [InlineData("--family", "expr:option_buy", "--run", "a", "--profile", "b")]
    [InlineData("--family", "expr:option_buy", "--family", "expr:option_buy", "--run", "a")]
    [InlineData("--family", "expr:option_buy", "--run", "a", "--holdout", "--holdout")]
    [InlineData("--family", "expr:option_buy", "--run")]
    [InlineData("--family", "expr:option_buy", "--run", "a", "--spread", "1")]
    public void A_bad_line_is_a_usage_error(params string[] args)
    {
        var parsed = OptionsExpressionEvalCommand.Parse(args);

        parsed.Command.ShouldBeNull();
        parsed.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_usage_names_the_exit_codes_the_global_usage_lists_the_verb_and_the_family_constant_is_the_domain_family()
    {
        OptionsExpressionEvalCommand.Usage.ShouldContain("exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled");
        OptionsExpressionEvalCommand.Usage.ShouldContain("BEFORE any figure is read");
        XdOptions.Usage.ShouldContain("xd options expression-eval --family expr:option_buy|expr:debit_spread|expr:credit_spread|expr:iron_condor --run RUNKEY [--profile HASH] [--holdout]");
        EvaluateExpressionCommand.OptionBuyFamily.ShouldBe(ExpressionFamilies.OptionBuy);
    }
}
