using XDrishti.Api.Cli;
using XDrishti.Application.Pairs;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd pairs run</c> and <c>xd pairs eval</c> (spec P14-13 contract): pure parsing, no database. A bad line is exit 2 (the usage error).</summary>
public sealed class PairsCommandTests
{
    [Fact]
    public void The_run_verb_takes_two_dates_and_optionally_variants_of_the_grid()
    {
        var errors = new List<string>();

        var all = PairsCommand.ParseRun(["--from", "2025-01-01", "--to", "2025-12-31"], errors);
        var one = PairsCommand.ParseRun(["--from", "2025-01-01", "--to", "2025-12-31", "--variant", "2.0,5", "--variant", "1.5,10"], errors);

        errors.ShouldBeEmpty();
        all.ShouldBe(new RunPairsCommand(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), null));
        one!.Variants.ShouldBe([PairVariant.Primary, new PairVariant(1.5, 10)]);
    }

    [Theory]
    [InlineData]
    [InlineData("--from", "2025-01-01")]
    [InlineData("--to", "2025-12-31")]
    [InlineData("--from", "2025-12-31", "--to", "2025-01-01")]
    [InlineData("--from", "2025-1-1", "--to", "2025-12-31")]
    [InlineData("--from", "2025-01-01", "--from", "2025-01-02", "--to", "2025-12-31")]
    [InlineData("--from", "2025-01-01", "--to", "2025-12-31", "--variant", "3.0,5")]
    [InlineData("--from", "2025-01-01", "--to", "2025-12-31", "--variant", "2.0,5", "--variant", "2.0,5")]
    [InlineData("--from", "2025-01-01", "--to", "2025-12-31", "--variant")]
    [InlineData("--from", "2025-01-01", "--to", "2025-12-31", "--notional", "5")]
    public void A_bad_run_line_is_a_usage_error(params string[] args)
    {
        var errors = new List<string>();

        PairsCommand.ParseRun(args, errors).ShouldBeNull();

        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_eval_verb_takes_the_family_and_the_hold_out_flag()
    {
        var errors = new List<string>();

        var plain = PairsCommand.ParseEval(["--family", "pairs:intraday-1"], errors);
        var hold = PairsCommand.ParseEval(["--family", "pairs:intraday-1", "--holdout"], errors);

        errors.ShouldBeEmpty();
        plain.ShouldBe(new EvaluatePairsCommand("pairs:intraday-1", false));
        hold.ShouldBe(new EvaluatePairsCommand("pairs:intraday-1", true));
    }

    [Theory]
    [InlineData]
    [InlineData("--holdout")]
    [InlineData("--family", "pead")]
    [InlineData("--family", "pairs:intraday-1", "--family", "pairs:intraday-1")]
    [InlineData("--family")]
    [InlineData("--family", "pairs:intraday-1", "--variant", "2.0,5")]
    public void A_bad_eval_line_is_a_usage_error(params string[] args)
    {
        var errors = new List<string>();

        PairsCommand.ParseEval(args, errors).ShouldBeNull();

        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_usage_texts_name_the_exit_codes_and_the_global_usage_lists_both_verbs()
    {
        PairsCommand.Usage.ShouldContain("exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled");
        XdOptions.Usage.ShouldContain("xd pairs run --from YYYY-MM-DD --to YYYY-MM-DD [--variant E,L]");
        XdOptions.Usage.ShouldContain("xd pairs eval --family pairs:intraday-1 [--holdout]");
    }

    [Fact]
    public void The_family_constant_of_the_command_is_the_domain_family()
    {
        EvaluatePairsCommand.IntradayFamily.ShouldBe(XDrishti.Domain.Learning.Extensions.PairFamilies.Intraday);
    }
}
